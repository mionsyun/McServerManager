namespace McServerManager.Models;

public sealed class AppUpdateManifest
{
    // Missing edition preserves compatibility with the existing Free update manifest.
    public string? Edition { get; set; }
    public string Version { get; set; } = string.Empty;
    public string InstallerUrl { get; set; } = string.Empty;
    public string Sha256 { get; set; } = string.Empty;
    public DateTimeOffset? PublishedAt { get; set; }
    public string? ReleaseNotesUrl { get; set; }
}
