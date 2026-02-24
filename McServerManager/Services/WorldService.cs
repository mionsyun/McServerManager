using System.IO.Compression;
using McServerManager.Models;

namespace McServerManager.Services;

public sealed class WorldService
{
    public IReadOnlyList<string> GetWorlds(string serverDirectory)
    {
        if (!Directory.Exists(serverDirectory))
        {
            return Array.Empty<string>();
        }

        var excluded = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "logs",
            "libraries",
            "versions",
            "cache"
        };

        var worlds = new List<string>();
        foreach (var dir in Directory.EnumerateDirectories(serverDirectory))
        {
            var name = Path.GetFileName(dir);
            if (excluded.Contains(name))
            {
                continue;
            }

            if (IsWorldDirectory(dir))
            {
                worlds.Add(name);
            }
        }

        return worlds.OrderBy(name => name).ToList();
    }

    public void CreateWorldFolder(string serverDirectory, string worldName)
    {
        ValidateWorldName(worldName);

        var path = Path.Combine(serverDirectory, worldName);
        if (Directory.Exists(path))
        {
            throw new InvalidOperationException("同名のワールドが既に存在します。");
        }

        Directory.CreateDirectory(path);
    }

    public void DeleteWorld(string serverDirectory, string worldName)
    {
        var path = Path.Combine(serverDirectory, worldName);
        if (Directory.Exists(path))
        {
            Directory.Delete(path, true);
        }
    }

    public IReadOnlyList<ArchiveWorldCandidate> GetArchiveWorldCandidates(string archivePath)
    {
        ValidateArchivePath(archivePath);

        var candidatePaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        using var archive = ZipFile.OpenRead(archivePath);
        foreach (var entry in archive.Entries)
        {
            var normalizedPath = NormalizeArchiveEntryPath(entry.FullName);
            if (string.IsNullOrWhiteSpace(normalizedPath))
            {
                continue;
            }

            if (string.Equals(normalizedPath, "level.dat", StringComparison.OrdinalIgnoreCase))
            {
                candidatePaths.Add(string.Empty);
            }
            else if (normalizedPath.EndsWith("/level.dat", StringComparison.OrdinalIgnoreCase))
            {
                var parent = normalizedPath[..^"level.dat".Length].TrimEnd('/');
                candidatePaths.Add(parent);
            }

            TryAddRegionCandidate(normalizedPath, candidatePaths);
        }

        return candidatePaths
            .Select(path => path.Trim('/'))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(GetArchiveDepth)
            .ThenBy(path => path.Length)
            .ThenBy(path => path, StringComparer.OrdinalIgnoreCase)
            .Select(path => new ArchiveWorldCandidate(
                path,
                string.IsNullOrWhiteSpace(path)
                    ? "(ZIP ルート)"
                    : path.Replace('/', Path.DirectorySeparatorChar)))
            .ToList();
    }

    public string ImportWorldArchive(
        string serverDirectory,
        string archivePath,
        string targetWorldName,
        bool overwriteExisting,
        string? sourceWorldRelativePath = null)
    {
        if (string.IsNullOrWhiteSpace(serverDirectory) || !Directory.Exists(serverDirectory))
        {
            throw new DirectoryNotFoundException("サーバーディレクトリが見つかりません。");
        }

        ValidateArchivePath(archivePath);

        ValidateWorldName(targetWorldName);

        var destination = Path.Combine(serverDirectory, targetWorldName.Trim());
        if (Directory.Exists(destination))
        {
            if (!overwriteExisting)
            {
                throw new InvalidOperationException("導入先ワールドが既に存在します。上書き設定を有効にしてください。");
            }

            Directory.Delete(destination, true);
        }

        var tempRoot = Path.Combine(Path.GetTempPath(), $"MaiPilot-world-import-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempRoot);

        try
        {
            ZipFile.ExtractToDirectory(archivePath, tempRoot, overwriteFiles: true);
            var worldRoot = ResolveWorldRoot(tempRoot, sourceWorldRelativePath);
            CopyDirectory(worldRoot, destination);
            return GetSourceLabel(tempRoot, worldRoot);
        }
        finally
        {
            try
            {
                if (Directory.Exists(tempRoot))
                {
                    Directory.Delete(tempRoot, true);
                }
            }
            catch
            {
                // Ignore cleanup failures.
            }
        }
    }

    private static void ValidateArchivePath(string archivePath)
    {
        if (string.IsNullOrWhiteSpace(archivePath) || !File.Exists(archivePath))
        {
            throw new FileNotFoundException("配布マップZIPが見つかりません。", archivePath);
        }

        if (!string.Equals(Path.GetExtension(archivePath), ".zip", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("配布マップは .zip 形式で指定してください。");
        }
    }

    private static void ValidateWorldName(string worldName)
    {
        if (string.IsNullOrWhiteSpace(worldName))
        {
            throw new InvalidOperationException("ワールド名を入力してください。");
        }

        var trimmed = worldName.Trim();
        if (trimmed.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
        {
            throw new InvalidOperationException("ワールド名に使用できない文字が含まれています。");
        }

        if (trimmed is "." or "..")
        {
            throw new InvalidOperationException("ワールド名が不正です。");
        }
    }

    private static string ResolveWorldRoot(string extractedRoot, string? sourceWorldRelativePath)
    {
        if (sourceWorldRelativePath is not null)
        {
            var selectedRoot = ResolveSelectedSourcePath(extractedRoot, sourceWorldRelativePath);
            if (!Directory.Exists(selectedRoot) || !IsWorldDirectory(selectedRoot))
            {
                throw new InvalidOperationException("選択した導入元フォルダにワールドデータが見つかりません。");
            }

            return selectedRoot;
        }

        var candidates = new List<string>();
        if (IsWorldDirectory(extractedRoot))
        {
            candidates.Add(extractedRoot);
        }

        foreach (var dir in Directory.EnumerateDirectories(extractedRoot, "*", SearchOption.AllDirectories))
        {
            if (IsWorldDirectory(dir))
            {
                candidates.Add(dir);
            }
        }

        if (candidates.Count == 0)
        {
            throw new InvalidOperationException("ZIP内にワールドデータ（level.dat または region）が見つかりません。");
        }

        return candidates
            .OrderBy(path => GetDepth(extractedRoot, path))
            .ThenBy(path => path.Length)
            .First();
    }

    private static string ResolveSelectedSourcePath(string extractedRoot, string sourceWorldRelativePath)
    {
        var fullRoot = Path.GetFullPath(extractedRoot);
        var selectedRoot = string.IsNullOrWhiteSpace(sourceWorldRelativePath)
            ? fullRoot
            : Path.GetFullPath(Path.Combine(
                fullRoot,
                sourceWorldRelativePath
                    .Replace('/', Path.DirectorySeparatorChar)
                    .Replace('\\', Path.DirectorySeparatorChar)));

        var relative = Path.GetRelativePath(fullRoot, selectedRoot);
        if (relative == ".."
            || relative.StartsWith($"..{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
            || relative.StartsWith($"..{Path.AltDirectorySeparatorChar}", StringComparison.Ordinal))
        {
            throw new InvalidOperationException("選択した導入元フォルダが不正です。");
        }

        return selectedRoot;
    }

    private static bool IsWorldDirectory(string path)
    {
        return File.Exists(Path.Combine(path, "level.dat"))
               || Directory.Exists(Path.Combine(path, "region"));
    }

    private static string NormalizeArchiveEntryPath(string entryPath)
    {
        return entryPath.Replace('\\', '/').Trim('/').Trim();
    }

    private static void TryAddRegionCandidate(string normalizedPath, ISet<string> candidatePaths)
    {
        if (normalizedPath.StartsWith("region/", StringComparison.OrdinalIgnoreCase)
            || string.Equals(normalizedPath, "region", StringComparison.OrdinalIgnoreCase))
        {
            candidatePaths.Add(string.Empty);
            return;
        }

        const string marker = "/region/";
        var markerIndex = normalizedPath.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
        if (markerIndex >= 0)
        {
            candidatePaths.Add(normalizedPath[..markerIndex].Trim('/'));
            return;
        }

        if (normalizedPath.EndsWith("/region", StringComparison.OrdinalIgnoreCase))
        {
            candidatePaths.Add(normalizedPath[..^"/region".Length].Trim('/'));
        }
    }

    private static int GetArchiveDepth(string relativePath)
    {
        if (string.IsNullOrWhiteSpace(relativePath))
        {
            return 0;
        }

        return relativePath.Split('/', StringSplitOptions.RemoveEmptyEntries).Length;
    }

    private static string GetSourceLabel(string extractedRoot, string worldRoot)
    {
        var relative = Path.GetRelativePath(extractedRoot, worldRoot);
        return relative is "." or ""
            ? "(ZIP ルート)"
            : relative.Replace(Path.DirectorySeparatorChar, '/').Replace(Path.AltDirectorySeparatorChar, '/');
    }

    private static int GetDepth(string root, string path)
    {
        var relative = Path.GetRelativePath(root, path);
        if (relative is "." or "")
        {
            return 0;
        }

        return relative
            .Split(new[] { Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar }, StringSplitOptions.RemoveEmptyEntries)
            .Length;
    }

    private static void CopyDirectory(string source, string destination)
    {
        Directory.CreateDirectory(destination);

        foreach (var file in Directory.EnumerateFiles(source))
        {
            var targetFile = Path.Combine(destination, Path.GetFileName(file));
            File.Copy(file, targetFile, true);
        }

        foreach (var directory in Directory.EnumerateDirectories(source))
        {
            var targetDirectory = Path.Combine(destination, Path.GetFileName(directory));
            CopyDirectory(directory, targetDirectory);
        }
    }
}

