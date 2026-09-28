namespace McServerManager.Models;

/// <summary>Bedrock Dedicated Server (統合版サーバー) の配布版情報。</summary>
public sealed class BedrockVersionInfo
{
    public string Version { get; set; } = string.Empty;
    public string DownloadUrl { get; set; } = string.Empty;
    public bool IsPreview { get; set; }
}
