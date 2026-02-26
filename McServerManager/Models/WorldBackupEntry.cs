namespace McServerManager.Models;

public sealed class WorldBackupEntry
{
    public string FileName { get; init; } = string.Empty;
    public string FullPath { get; init; } = string.Empty;
    public string WorldName { get; init; } = string.Empty;
    public DateTime CreatedAt { get; init; }
    public long FileSizeBytes { get; init; }

    public string SizeText => $"{FileSizeBytes / 1024d / 1024d:F1} MB";
    public string CreatedAtText => CreatedAt.ToString("yyyy/MM/dd HH:mm:ss");
}
