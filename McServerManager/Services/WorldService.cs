using System.IO.Compression;
using System.Text;
using System.Text.Json;
using McServerManager.Models;

namespace McServerManager.Services;

public sealed class WorldService
{
    private readonly JsonSerializerOptions _options = new() { WriteIndented = true };

    public IReadOnlyList<string> GetWorlds(string serverDirectory)
    {
        if (!Directory.Exists(serverDirectory))
        {
            return Array.Empty<string>();
        }

        var excluded = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "logs", "backups", "libraries", "versions", "cache"
        };

        var worlds = new List<string>();
        foreach (var dir in Directory.EnumerateDirectories(serverDirectory))
        {
            var name = Path.GetFileName(dir);
            if (excluded.Contains(name))
            {
                continue;
            }

            var hasLevelDat = File.Exists(Path.Combine(dir, "level.dat"));
            var hasRegion = Directory.Exists(Path.Combine(dir, "region"));
            if (hasLevelDat || hasRegion)
            {
                worlds.Add(name);
            }
        }

        return worlds.OrderBy(name => name).ToList();
    }

    public void CreateWorldFolder(string serverDirectory, string worldName)
    {
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

    public BackupMetadata BackupWorld(string serverDirectory, string worldName, string backupsDirectory, string comment)
    {
        Directory.CreateDirectory(backupsDirectory);

        var timestamp = DateTime.Now.ToString("yyyyMMdd-HHmmss");
        var safeComment = SanitizeFileName(comment);
        var fileName = string.IsNullOrWhiteSpace(safeComment)
            ? $"{worldName}_{timestamp}.zip"
            : $"{worldName}_{timestamp}_{safeComment}.zip";

        var backupPath = Path.Combine(backupsDirectory, fileName);
        var worldPath = Path.Combine(serverDirectory, worldName);

        if (!Directory.Exists(worldPath))
        {
            throw new DirectoryNotFoundException("ワールドフォルダが見つかりません。");
        }

        ZipFile.CreateFromDirectory(worldPath, backupPath, CompressionLevel.Fastest, false, Encoding.UTF8);

        var metadata = new BackupMetadata
        {
            FileName = fileName,
            WorldName = worldName,
            Comment = comment ?? string.Empty,
            CreatedAt = DateTime.UtcNow
        };

        SaveBackupMetadata(backupsDirectory, metadata);
        WriteMetadataToZip(backupPath, metadata);

        return metadata;
    }

    public IReadOnlyList<BackupMetadata> GetBackups(string backupsDirectory)
    {
        if (!Directory.Exists(backupsDirectory))
        {
            return Array.Empty<BackupMetadata>();
        }

        var list = new List<BackupMetadata>();
        foreach (var file in Directory.EnumerateFiles(backupsDirectory, "*.zip"))
        {
            var metadataPath = Path.ChangeExtension(file, ".json");
            if (File.Exists(metadataPath))
            {
                try
                {
                    var json = File.ReadAllText(metadataPath);
                    var metadata = JsonSerializer.Deserialize<BackupMetadata>(json);
                    if (metadata is not null)
                    {
                        list.Add(metadata);
                        continue;
                    }
                }
                catch
                {
                    // Fallback below
                }
            }

            list.Add(new BackupMetadata
            {
                FileName = Path.GetFileName(file),
                WorldName = "",
                Comment = "",
                CreatedAt = File.GetCreationTimeUtc(file)
            });
        }

        return list.OrderByDescending(b => b.CreatedAt).ToList();
    }

    public void RestoreBackup(string serverDirectory, string backupsDirectory, BackupMetadata backup, string targetWorldName)
    {
        var backupPath = Path.Combine(backupsDirectory, backup.FileName);
        if (!File.Exists(backupPath))
        {
            throw new FileNotFoundException("バックアップが見つかりません。", backupPath);
        }

        var worldPath = Path.Combine(serverDirectory, targetWorldName);
        if (Directory.Exists(worldPath))
        {
            Directory.Delete(worldPath, true);
        }

        Directory.CreateDirectory(worldPath);
        ZipFile.ExtractToDirectory(backupPath, worldPath, true);
    }

    private void SaveBackupMetadata(string backupsDirectory, BackupMetadata metadata)
    {
        var metadataPath = Path.Combine(backupsDirectory, Path.ChangeExtension(metadata.FileName, ".json"));
        var json = JsonSerializer.Serialize(metadata, _options);
        File.WriteAllText(metadataPath, json);
    }

    private static string SanitizeFileName(string? input)
    {
        if (string.IsNullOrWhiteSpace(input))
        {
            return string.Empty;
        }

        foreach (var c in Path.GetInvalidFileNameChars())
        {
            input = input.Replace(c, '_');
        }

        return input.Trim();
    }

    private static void WriteMetadataToZip(string backupPath, BackupMetadata metadata)
    {
        using var archive = ZipFile.Open(backupPath, ZipArchiveMode.Update);
        var entry = archive.CreateEntry("backup-info.txt");
        using var writer = new StreamWriter(entry.Open(), Encoding.UTF8);
        writer.WriteLine($"World: {metadata.WorldName}");
        writer.WriteLine($"CreatedUtc: {metadata.CreatedAt:O}");
        writer.WriteLine($"Comment: {metadata.Comment}");
    }
}
