namespace McServerManager.Models;

public sealed class AddonEntry
{
    public string FileName { get; init; } = string.Empty;
    public string FullPath { get; init; } = string.Empty;
    public bool IsEnabled { get; init; }
    public long FileSizeBytes { get; init; }
    public DateTime LastUpdatedAt { get; init; }
    public string EnabledText => IsEnabled ? "有効" : "無効";
}
