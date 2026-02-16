namespace McServerManager.Models;

public sealed class MinecraftVersionInfo
{
    public string Id { get; set; } = string.Empty;
    public string Type { get; set; } = string.Empty;
    public DateTime ReleaseTime { get; set; }
    public string ManifestUrl { get; set; } = string.Empty;
}
