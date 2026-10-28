using System.IO;
using System.Threading;
using McServerManager.Services.Templates;

namespace McServerManager.Services.Participants;

/// <summary>
/// Bounded, explicit-path I/O for a cooperative, non-hostile same-user filesystem.
/// Reparse-point checks are best effort: portable path APIs cannot prevent a hostile process
/// replacing an ancestor between validation and opening/moving a file. They are not a sandbox.
/// A ZIP is staged in its destination directory and committed with a no-overwrite native move.
/// No fallback copies, directory creation, destination deletion or replacement are performed.
/// </summary>
public sealed class ParticipantExportFileService : IParticipantExportFileService
{
    private readonly IParticipantExportFileOperations _operations;

    public ParticipantExportFileService() : this(new ParticipantExportFileOperations()) { }

    internal ParticipantExportFileService(IParticipantExportFileOperations operations) =>
        _operations = operations ?? throw new ArgumentNullException(nameof(operations));

    public Task<byte[]> ReadDefinitionAsync(string path, CancellationToken cancellationToken = default) =>
        ReadBoundedAsync(path, ".json", ParticipantDefinitionPolicy.MaxDefinitionBytes,
            "クライアント定義 JSON は 256 KiB 以下にしてください。", cancellationToken);

    public Task<byte[]> ReadTemplateAsync(string path, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        cancellationToken.ThrowIfCancellationRequested();
        if (!path.EndsWith(".maipilot-template.json", StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException(".maipilot-template.json ファイルを選択してください。", nameof(path));
        return ReadBoundedAsync(path, ".json", TemplatePolicy.MaxManifestBytes,
            "テンプレートは 1 MiB 以下にしてください。", cancellationToken);
    }

    private static async Task<byte[]> ReadBoundedAsync(string path, string extension, int maximumBytes,
        string sizeError, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var fullPath = NormalizePath(path, extension);
        CheckRegularFile(fullPath);
        await using var file = new FileStream(fullPath, FileMode.Open, FileAccess.Read, FileShare.Read,
            8192, FileOptions.Asynchronous | FileOptions.SequentialScan);
        CheckRegularFile(fullPath);
        if (file.Length > maximumBytes)
            throw new IOException(sizeError);

        // One sentinel byte detects growth after Length was checked without an unbounded read.
        var buffer = new byte[maximumBytes + 1];
        var total = 0;
        while (total < buffer.Length)
        {
            var read = await file.ReadAsync(buffer.AsMemory(total), cancellationToken).ConfigureAwait(false);
            if (read == 0) break;
            total += read;
            if (total > maximumBytes) throw new IOException(sizeError);
        }
        cancellationToken.ThrowIfCancellationRequested();
        return buffer.AsSpan(0, total).ToArray();
    }

    public Task<string> SaveNewZipAsync(string path, ReadOnlyMemory<byte> bytes,
        CancellationToken cancellationToken = default) =>
        SaveNewFileAsync(path, bytes, ".zip", ParticipantDefinitionPolicy.MaxZipBytes, null, cancellationToken);

    // Internal reuse keeps participant ZIP and Pro declaration publication on the same tested
    // no-overwrite boundary. The Pro-facing service owns validation and capability checks.
    internal Task<string> SaveNewTemplateAsync(string path, ReadOnlyMemory<byte> bytes,
        Action verifyAuthority, CancellationToken cancellationToken) =>
        SaveNewFileAsync(path, bytes, ".json", TemplatePolicy.MaxManifestBytes, verifyAuthority, cancellationToken);

    private async Task<string> SaveNewFileAsync(string path, ReadOnlyMemory<byte> bytes,
        string extension, int maximumBytes, Action? verifyAuthority, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        verifyAuthority?.Invoke();
        var fullPath = NormalizePath(path, extension);
        if (bytes.IsEmpty || bytes.Length > maximumBytes)
            throw new ArgumentException("出力ファイルのサイズが許容範囲外です。", nameof(bytes));
        CheckNewDestination(fullPath);
        var directory = Path.GetDirectoryName(fullPath)!;
        var stagePath = Path.Combine(directory, $".maipilot-participant-{Guid.NewGuid():N}.tmp");
        // Own a bounded snapshot rather than borrowing mutable caller memory across awaits.
        var contents = bytes.ToArray();
        var ownsStage = false;
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            CheckAncestors(stagePath);
            await using (var stage = new FileStream(stagePath, FileMode.CreateNew, FileAccess.Write,
                FileShare.None, 8192, FileOptions.Asynchronous | FileOptions.SequentialScan))
            {
                ownsStage = true;
                await _operations.WriteAsync(stage, contents, cancellationToken).ConfigureAwait(false);
                await stage.FlushAsync(cancellationToken).ConfigureAwait(false);
                stage.Flush(flushToDisk: true);
            }

            // Retry only transient Windows sharing/lock errors, and recheck every boundary.
            // Other failures retain their original type and are never reported as success.
            for (var attempt = 0; ; attempt++)
            {
                CheckRegularFile(stagePath);
                CheckNewDestination(fullPath);
                cancellationToken.ThrowIfCancellationRequested();
                verifyAuthority?.Invoke();
                try
                {
                    _operations.MoveNew(stagePath, fullPath);
                    ownsStage = false;
                    // The move is the commit point. Do not observe cancellation after it.
                    return fullPath;
                }
                catch (IOException ex) when (attempt < 2 && IsSharingViolation(ex))
                {
                    await Task.Delay(50, cancellationToken).ConfigureAwait(false);
                }
            }
        }
        catch (Exception failure)
        {
            if (ownsStage)
            {
                try
                {
                    CheckAncestors(stagePath);
                    var attributes = TryGetAttributes(stagePath);
                    if (attributes is not null)
                    {
                        RejectLinkOrDirectory(attributes.Value);
                        _operations.DeleteStage(stagePath);
                    }
                }
                catch (Exception cleanupFailure) when (cleanupFailure is IOException or UnauthorizedAccessException)
                {
                    // Do not hide failure to remove our own partial file behind cancellation.
                    throw new ParticipantExportCleanupException(stagePath, failure, cleanupFailure);
                }
            }
            throw;
        }
    }

    private static string NormalizePath(string path, string extension)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        var fullPath = Path.GetFullPath(path);
        if (!string.Equals(Path.GetExtension(fullPath), extension, StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException($"{extension} ファイルを選択してください。", nameof(path));

        // Reject Windows alternate data streams/device aliases; they are not ordinary files.
        if (OperatingSystem.IsWindows())
        {
            if (fullPath.StartsWith(@"\\?\", StringComparison.Ordinal)
                || fullPath.StartsWith(@"\\.\", StringComparison.Ordinal))
                throw new ArgumentException("通常のファイルパスを選択してください。", nameof(path));
            var relative = fullPath[Path.GetPathRoot(fullPath)!.Length..];
            foreach (var component in relative.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar))
            {
                var stem = component.Split('.')[0];
                if (component.Contains(':') || component.EndsWith(' ') || component.EndsWith('.')
                    || new[] { "CON", "PRN", "AUX", "NUL", "CONIN$", "CONOUT$" }.Contains(stem, StringComparer.OrdinalIgnoreCase)
                    || (stem.Length == 4 && (stem.StartsWith("COM", StringComparison.OrdinalIgnoreCase)
                        || stem.StartsWith("LPT", StringComparison.OrdinalIgnoreCase)) && "123456789¹²³".Contains(stem[3])))
                    throw new ArgumentException("通常のファイルパスを選択してください。", nameof(path));
            }
        }
        return fullPath;
    }

    private static void CheckAncestors(string path)
    {
        var ancestors = new Stack<string>();
        for (var directory = Path.GetDirectoryName(path); directory is not null; directory = Path.GetDirectoryName(directory))
            ancestors.Push(directory);
        // Inspect parents before children so no already-detected link is traversed.
        while (ancestors.TryPop(out var directory))
        {
            var attributes = File.GetAttributes(directory);
            if ((attributes & FileAttributes.ReparsePoint) != 0)
                throw new IOException("リンクまたは再解析ポイントを含むパスは使用できません。");
            if ((attributes & FileAttributes.Directory) == 0)
                throw new IOException("保存先または入力元の親パスがフォルダーではありません。");
        }
    }

    private static void CheckRegularFile(string path)
    {
        CheckAncestors(path);
        RejectLinkOrDirectory(File.GetAttributes(path));
    }

    private static void CheckNewDestination(string path)
    {
        CheckAncestors(path);
        if (TryGetAttributes(path) is not null)
            throw new IOException("同じ名前のファイルまたはフォルダーが存在します。新しいファイル名を選択してください。");
    }

    private static FileAttributes? TryGetAttributes(string path)
    {
        try { return File.GetAttributes(path); }
        catch (FileNotFoundException) { return null; }
    }

    private static void RejectLinkOrDirectory(FileAttributes attributes)
    {
        if ((attributes & (FileAttributes.ReparsePoint | FileAttributes.Directory)) != 0)
            throw new IOException("通常のファイルを選択してください。リンクやフォルダーは使用できません。");
    }

    private static bool IsSharingViolation(IOException exception) =>
        OperatingSystem.IsWindows() && (exception.HResult & 0xffff) is 32 or 33;
}

/// <summary>A save failed and its owned temporary file could not be safely removed.</summary>
public sealed class ParticipantExportCleanupException : IOException
{
    internal ParticipantExportCleanupException(string stagePath, Exception failure, Exception cleanupFailure)
        : base("ファイルの保存に失敗し、一時ファイルを削除できませんでした。", new AggregateException(failure, cleanupFailure)) =>
        StagePath = stagePath;

    public string StagePath { get; }
}

// A narrow I/O seam makes cancellation and commit failures reproducible with synthetic files.
internal interface IParticipantExportFileOperations
{
    Task WriteAsync(Stream stage, ReadOnlyMemory<byte> bytes, CancellationToken cancellationToken);
    void MoveNew(string stagePath, string destinationPath);
    void DeleteStage(string stagePath);
}

internal sealed class ParticipantExportFileOperations : IParticipantExportFileOperations
{
    public Task WriteAsync(Stream stage, ReadOnlyMemory<byte> bytes, CancellationToken cancellationToken) =>
        stage.WriteAsync(bytes, cancellationToken).AsTask();

    public void MoveNew(string stagePath, string destinationPath) => File.Move(stagePath, destinationPath, overwrite: false);
    public void DeleteStage(string stagePath) => File.Delete(stagePath);
}
