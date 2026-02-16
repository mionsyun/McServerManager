namespace McServerManager.Models;

public sealed class BackupMetadata
{
    public string FileName { get; set; } = string.Empty;
    public string WorldName { get; set; } = string.Empty;
    public string Comment { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
