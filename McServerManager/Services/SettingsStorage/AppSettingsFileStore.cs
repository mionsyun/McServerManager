using System.Diagnostics;
using System.IO;

namespace McServerManager.Services;

/// <summary>
/// Cooperative, same-user storage. The persistent lock file must never be removed on release:
/// unlinking it could allow two processes to lock different files with the same name.
/// </summary>
internal sealed class AppSettingsFileStore
{
    internal const int MaxFileBytes = 1024 * 1024;
    private readonly string _path;
    private readonly IAppSettingsCommitOperations _commitOperations;
    private readonly TimeSpan _lockTimeout;

    internal AppSettingsFileStore(string path, IAppSettingsCommitOperations? commitOperations = null,
        TimeSpan? lockTimeout = null)
    {
        _path = Path.GetFullPath(path);
        _commitOperations = commitOperations ?? new AppSettingsCommitOperations();
        _lockTimeout = lockTimeout ?? TimeSpan.FromSeconds(2);
    }

    internal IDisposable AcquireLock()
    {
        var timer = Stopwatch.StartNew();
        while (true)
        {
            try
            {
                return new FileStream(_path + ".lock", FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
            }
            catch (DirectoryNotFoundException ex)
            {
                throw new AppSettingsStorageException(AppSettingsStorageError.ReadFailed, ex);
            }
            catch (IOException ex)
            {
                if (timer.Elapsed >= _lockTimeout)
                    throw new AppSettingsStorageException(AppSettingsStorageError.Busy, ex);
                Thread.Sleep(25);
            }
            catch (UnauthorizedAccessException ex)
            {
                throw new AppSettingsStorageException(AppSettingsStorageError.ReadFailed, ex);
            }
        }
    }

    internal byte[]? Read()
    {
        try
        {
            using var file = new FileStream(_path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            if (file.Length > MaxFileBytes)
                throw new AppSettingsStorageException(AppSettingsStorageError.TooLarge);
            using var buffer = new MemoryStream();
            var chunk = new byte[8192];
            int count;
            while ((count = file.Read(chunk, 0, Math.Min(chunk.Length, MaxFileBytes + 1 - (int)buffer.Length))) > 0)
            {
                buffer.Write(chunk, 0, count);
                if (buffer.Length > MaxFileBytes)
                    throw new AppSettingsStorageException(AppSettingsStorageError.TooLarge);
            }
            return buffer.ToArray();
        }
        catch (FileNotFoundException)
        {
            // A missing primary with a backup is a recovery case, not a fresh installation.
            try
            {
                File.GetAttributes(_path + ".bak");
            }
            catch (FileNotFoundException) { return null; }
            catch (DirectoryNotFoundException) { return null; }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                throw new AppSettingsStorageException(AppSettingsStorageError.ReadFailed, ex);
            }
            throw new AppSettingsStorageException(AppSettingsStorageError.InvalidData);
        }
        catch (AppSettingsStorageException) { throw; }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new AppSettingsStorageException(AppSettingsStorageError.ReadFailed, ex);
        }
    }

    internal void Write(byte[] contents, bool replaceExisting)
    {
        if (contents.Length > MaxFileBytes)
            throw new AppSettingsStorageException(AppSettingsStorageError.TooLarge);

        // Same-directory staging is required for the native rename/replace operation.
        var temporaryPath = _path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            _commitOperations.WriteTemporaryFile(temporaryPath, contents);
            if (replaceExisting)
                _commitOperations.ReplaceFile(temporaryPath, _path, _path + ".bak");
            else
                _commitOperations.MoveFile(temporaryPath, _path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            // No direct-write fallback or automatic restore: a failed native replacement may
            // have an ambiguous outcome on an unsupported filesystem. Preserve the evidence.
            throw new AppSettingsStorageException(AppSettingsStorageError.WriteFailed, ex);
        }
        finally
        {
            try { File.Delete(temporaryPath); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        }
    }
}

internal interface IAppSettingsCommitOperations
{
    void WriteTemporaryFile(string path, byte[] contents);
    void ReplaceFile(string temporaryPath, string settingsPath, string backupPath);
    void MoveFile(string temporaryPath, string settingsPath);
}

internal sealed class AppSettingsCommitOperations : IAppSettingsCommitOperations
{
    public void WriteTemporaryFile(string path, byte[] contents)
    {
        using var file = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None);
        file.Write(contents);
        file.Flush(flushToDisk: true);
    }

    public void ReplaceFile(string temporaryPath, string settingsPath, string backupPath) =>
        File.Replace(temporaryPath, settingsPath, backupPath, ignoreMetadataErrors: false);

    public void MoveFile(string temporaryPath, string settingsPath) => File.Move(temporaryPath, settingsPath);
}
