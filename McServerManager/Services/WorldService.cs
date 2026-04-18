using System.Globalization;
using System.IO.Compression;
using System.Text.Json;
using McServerManager.Models;

namespace McServerManager.Services;

public sealed class WorldService : IWorldService
{
    private const string BackupInfoEntryName = "backup-info.txt";
    private const string BackupMetadataEntryName = "backup-metadata.json";
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

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
            "cache",
            "backups"
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

    public string GetBackupsDirectory(string serverDirectory)
    {
        return Path.Combine(serverDirectory, "backups");
    }

    public IReadOnlyList<WorldBackupEntry> GetWorldBackups(string serverDirectory)
    {
        var backupsDirectory = GetBackupsDirectory(serverDirectory);
        if (!Directory.Exists(backupsDirectory))
        {
            return Array.Empty<WorldBackupEntry>();
        }

        var backups = new List<WorldBackupEntry>();
        foreach (var zipPath in Directory.EnumerateFiles(backupsDirectory, "*.zip", SearchOption.TopDirectoryOnly))
        {
            var fileInfo = new FileInfo(zipPath);
            var metadata = LoadBackupMetadata(zipPath);
            var createdAtUtc =
                metadata?.CreatedAtUtc
                ?? GuessCreatedAtUtcFromFileName(fileInfo.Name)
                ?? fileInfo.LastWriteTimeUtc;
            var worldName = metadata?.WorldName;
            if (string.IsNullOrWhiteSpace(worldName))
            {
                worldName = GuessWorldNameFromFileName(fileInfo.Name);
            }

            backups.Add(new WorldBackupEntry
            {
                FileName = fileInfo.Name,
                FullPath = zipPath,
                WorldName = worldName!,
                CreatedAt = createdAtUtc.ToLocalTime(),
                FileSizeBytes = fileInfo.Length,
            });
        }

        return backups
            .OrderByDescending(entry => entry.CreatedAt)
            .ThenByDescending(entry => entry.FileName, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public WorldBackupEntry CreateWorldBackup(string serverDirectory, string worldName)
    {
        return CreateWorldBackupInternal(serverDirectory, worldName, isAutomatic: false);
    }

    public WorldBackupEntry CreateAutomaticWorldBackup(string serverDirectory, string worldName, int maxRetention)
    {
        var entry = CreateWorldBackupInternal(serverDirectory, worldName, isAutomatic: true);
        if (maxRetention > 0)
        {
            PruneAutomaticBackups(serverDirectory, worldName, maxRetention);
        }
        return entry;
    }

    private WorldBackupEntry CreateWorldBackupInternal(string serverDirectory, string worldName, bool isAutomatic)
    {
        if (string.IsNullOrWhiteSpace(serverDirectory) || !Directory.Exists(serverDirectory))
        {
            throw new DirectoryNotFoundException("サーバーディレクトリが見つかりません。");
        }

        ValidateWorldName(worldName);
        var trimmedWorldName = worldName.Trim();
        var worldDirectory = Path.Combine(serverDirectory, trimmedWorldName);
        if (!Directory.Exists(worldDirectory) || !IsWorldDirectory(worldDirectory))
        {
            throw new InvalidOperationException("バックアップ対象のワールドデータが見つかりません。");
        }

        var backupsDirectory = GetBackupsDirectory(serverDirectory);
        Directory.CreateDirectory(backupsDirectory);

        var createdAtUtc = DateTime.UtcNow;
        var baseName = BuildBackupBaseName(backupsDirectory, trimmedWorldName, createdAtUtc, isAutomatic);
        var zipPath = Path.Combine(backupsDirectory, $"{baseName}.zip");
        var metadata = new BackupMetadata
        {
            WorldName = trimmedWorldName,
            CreatedAtUtc = createdAtUtc,
            IsAutomatic = isAutomatic,
        };

        using (var archive = ZipFile.Open(zipPath, ZipArchiveMode.Create))
        {
            var entryRoot = $"{trimmedWorldName}/";
            AddDirectoryToArchive(archive, worldDirectory, entryRoot);
            CreateTextEntry(archive, BackupInfoEntryName, BuildBackupInfo(metadata, serverDirectory));
            CreateTextEntry(archive, BackupMetadataEntryName, JsonSerializer.Serialize(metadata, JsonOptions));
        }

        SaveBackupMetadata(zipPath, metadata);

        var fileInfo = new FileInfo(zipPath);
        return new WorldBackupEntry
        {
            FileName = fileInfo.Name,
            FullPath = zipPath,
            WorldName = trimmedWorldName,
            CreatedAt = metadata.CreatedAtUtc.ToLocalTime(),
            FileSizeBytes = fileInfo.Length,
        };
    }

    private void PruneAutomaticBackups(string serverDirectory, string worldName, int maxRetention)
    {
        var backupsDirectory = GetBackupsDirectory(serverDirectory);
        if (!Directory.Exists(backupsDirectory))
        {
            return;
        }

        var trimmedWorldName = worldName.Trim();
        var automaticBackups = new List<(string ZipPath, DateTime CreatedAtUtc)>();

        foreach (var zipPath in Directory.EnumerateFiles(backupsDirectory, "*.zip", SearchOption.TopDirectoryOnly))
        {
            var metadata = LoadBackupMetadata(zipPath);
            if (metadata is null || !metadata.IsAutomatic)
            {
                continue;
            }
            if (!string.Equals(metadata.WorldName, trimmedWorldName, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }
            automaticBackups.Add((zipPath, metadata.CreatedAtUtc));
        }

        var excess = automaticBackups
            .OrderByDescending(entry => entry.CreatedAtUtc)
            .Skip(maxRetention)
            .ToList();

        foreach (var (zipPath, _) in excess)
        {
            try
            {
                DeleteWorldBackup(zipPath);
            }
            catch
            {
                // 個別失敗は無視し、次のファイル削除に進む。
            }
        }
    }

    public string RestoreWorldBackup(
        string serverDirectory,
        string backupZipPath,
        string targetWorldName,
        bool overwriteExisting,
        bool createBackupBeforeRestore)
    {
        if (string.IsNullOrWhiteSpace(serverDirectory) || !Directory.Exists(serverDirectory))
        {
            throw new DirectoryNotFoundException("サーバーディレクトリが見つかりません。");
        }

        ValidateBackupFilePath(backupZipPath);
        ValidateWorldName(targetWorldName);

        var normalizedTargetName = targetWorldName.Trim();
        var destination = Path.Combine(serverDirectory, normalizedTargetName);
        var destinationExists = Directory.Exists(destination);
        if (destinationExists && !overwriteExisting)
        {
            throw new InvalidOperationException("復元先ワールドが既に存在します。");
        }

        if (destinationExists && createBackupBeforeRestore)
        {
            CreateWorldBackup(serverDirectory, normalizedTargetName);
        }

        if (destinationExists)
        {
            Directory.Delete(destination, true);
        }

        var tempRoot = Path.Combine(Path.GetTempPath(), $"MaiPilot-world-restore-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempRoot);

        try
        {
            ZipFile.ExtractToDirectory(backupZipPath, tempRoot, overwriteFiles: true);
            var metadata = LoadBackupMetadata(backupZipPath);
            var worldRoot = ResolveBackupWorldRoot(tempRoot, metadata?.WorldName);
            CopyDirectory(worldRoot, destination);
            return metadata?.WorldName ?? Path.GetFileName(worldRoot);
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

    public void DeleteWorldBackup(string backupZipPath)
    {
        ValidateBackupFilePath(backupZipPath);
        File.Delete(backupZipPath);

        var sidecarPath = GetBackupMetadataPath(backupZipPath);
        if (File.Exists(sidecarPath))
        {
            File.Delete(sidecarPath);
        }
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

    private static void ValidateBackupFilePath(string backupZipPath)
    {
        if (string.IsNullOrWhiteSpace(backupZipPath) || !File.Exists(backupZipPath))
        {
            throw new FileNotFoundException("バックアップZIPが見つかりません。", backupZipPath);
        }

        if (!string.Equals(Path.GetExtension(backupZipPath), ".zip", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("バックアップは .zip 形式で指定してください。");
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

    private static void AddDirectoryToArchive(ZipArchive archive, string sourceDirectory, string entryRoot)
    {
        var normalizedEntryRoot = entryRoot.Trim('/');
        foreach (var file in Directory.EnumerateFiles(sourceDirectory, "*", SearchOption.AllDirectories))
        {
            var relativePath = Path.GetRelativePath(sourceDirectory, file).Replace('\\', '/');
            var entryPath = string.IsNullOrWhiteSpace(normalizedEntryRoot)
                ? relativePath
                : $"{normalizedEntryRoot}/{relativePath}";
            archive.CreateEntryFromFile(file, entryPath, CompressionLevel.Optimal);
        }
    }

    private static void CreateTextEntry(ZipArchive archive, string entryName, string content)
    {
        var entry = archive.CreateEntry(entryName, CompressionLevel.Optimal);
        using var writer = new StreamWriter(entry.Open());
        writer.Write(content);
    }

    private static string BuildBackupBaseName(string backupsDirectory, string worldName, DateTime createdAtUtc, bool isAutomatic)
    {
        var safeWorldName = SanitizeFileName(worldName);
        if (string.IsNullOrWhiteSpace(safeWorldName))
        {
            safeWorldName = "world";
        }

        var timestamp = createdAtUtc.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture);
        var prefix = isAutomatic ? "auto-" : string.Empty;
        var baseName = $"{prefix}{timestamp}-{safeWorldName}";
        var candidate = baseName;
        var index = 2;
        while (
            File.Exists(Path.Combine(backupsDirectory, $"{candidate}.zip"))
            || File.Exists(Path.Combine(backupsDirectory, $"{candidate}.json"))
        )
        {
            candidate = $"{baseName}-{index}";
            index += 1;
        }

        return candidate;
    }

    private static string SanitizeFileName(string value)
    {
        var invalidChars = Path.GetInvalidFileNameChars();
        return new string(value.Where(ch => !invalidChars.Contains(ch)).ToArray()).Trim();
    }

    private static string BuildBackupInfo(BackupMetadata metadata, string serverDirectory)
    {
        return string.Join(
            Environment.NewLine,
            [
                "MaiPilot world backup",
                $"CreatedAt(UTC): {metadata.CreatedAtUtc:O}",
                $"WorldName: {metadata.WorldName}",
                $"ServerDirectory: {serverDirectory}",
            ]
        );
    }

    private static string GetBackupMetadataPath(string backupZipPath)
    {
        return Path.ChangeExtension(backupZipPath, ".json");
    }

    private static void SaveBackupMetadata(string backupZipPath, BackupMetadata metadata)
    {
        var metadataPath = GetBackupMetadataPath(backupZipPath);
        var json = JsonSerializer.Serialize(metadata, JsonOptions);
        File.WriteAllText(metadataPath, json);
    }

    private static BackupMetadata? LoadBackupMetadata(string backupZipPath)
    {
        var metadataPath = GetBackupMetadataPath(backupZipPath);
        if (File.Exists(metadataPath))
        {
            try
            {
                var json = File.ReadAllText(metadataPath);
                var metadata = JsonSerializer.Deserialize<BackupMetadata>(json);
                if (metadata is not null)
                {
                    return metadata;
                }
            }
            catch
            {
                // Ignore metadata read failures and fall through to archive metadata.
            }
        }

        try
        {
            using var archive = ZipFile.OpenRead(backupZipPath);
            var metadataEntry = archive.GetEntry(BackupMetadataEntryName);
            if (metadataEntry is null)
            {
                return null;
            }

            using var reader = new StreamReader(metadataEntry.Open());
            var json = reader.ReadToEnd();
            return JsonSerializer.Deserialize<BackupMetadata>(json);
        }
        catch
        {
            return null;
        }
    }

    private static DateTime? GuessCreatedAtUtcFromFileName(string fileName)
    {
        var withoutExtension = Path.GetFileNameWithoutExtension(fileName);
        if (withoutExtension.Length < 15)
        {
            return null;
        }

        var timestamp = withoutExtension[..15];
        if (
            DateTime.TryParseExact(
                timestamp,
                "yyyyMMdd-HHmmss",
                CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
                out var parsed)
        )
        {
            return parsed;
        }

        return null;
    }

    private static string GuessWorldNameFromFileName(string fileName)
    {
        var withoutExtension = Path.GetFileNameWithoutExtension(fileName);
        if (withoutExtension.Length > 16 && withoutExtension[15] == '-')
        {
            var candidate = withoutExtension[16..];
            if (!string.IsNullOrWhiteSpace(candidate))
            {
                return candidate;
            }
        }

        return "world";
    }

    private static string ResolveBackupWorldRoot(string extractedRoot, string? preferredWorldName)
    {
        if (!string.IsNullOrWhiteSpace(preferredWorldName))
        {
            var preferredPath = Path.Combine(extractedRoot, preferredWorldName);
            if (Directory.Exists(preferredPath) && IsWorldDirectory(preferredPath))
            {
                return preferredPath;
            }
        }

        if (IsWorldDirectory(extractedRoot))
        {
            return extractedRoot;
        }

        var directCandidates = Directory
            .EnumerateDirectories(extractedRoot, "*", SearchOption.TopDirectoryOnly)
            .Where(IsWorldDirectory)
            .OrderBy(path => path.Length)
            .ToList();
        if (directCandidates.Count > 0)
        {
            return directCandidates[0];
        }

        var nestedCandidates = Directory
            .EnumerateDirectories(extractedRoot, "*", SearchOption.AllDirectories)
            .Where(IsWorldDirectory)
            .OrderBy(path => GetDepth(extractedRoot, path))
            .ThenBy(path => path.Length)
            .ToList();
        if (nestedCandidates.Count > 0)
        {
            return nestedCandidates[0];
        }

        throw new InvalidOperationException("バックアップZIP内にワールドデータが見つかりません。");
    }

    private sealed class BackupMetadata
    {
        public string WorldName { get; set; } = "world";
        public DateTime CreatedAtUtc { get; set; }
        public bool IsAutomatic { get; set; }
    }
}
