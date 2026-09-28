namespace McServerManager.Models;

public sealed class NewServerOptions
{
    public string Name { get; set; } = string.Empty;
    public string DirectoryPath { get; set; } = string.Empty;
    public string Type { get; set; } = "Vanilla";
    public string Version { get; set; } = "latest";
    public int MemoryXmsMb { get; set; } = 1024;
    public int MemoryXmxMb { get; set; } = 2048;
    public int Port { get; set; } = 25565;
    public int MaxPlayers { get; set; } = 20;
    public bool OnlineMode { get; set; } = true;
    public bool EnableCommandBlock { get; set; }
    public bool EulaAccepted { get; set; }
    public string JavaPath { get; set; } = string.Empty;
    public string Motd { get; set; } = "A Minecraft Server";

    // ─── 統合版 (Bedrock) 専用 ───
    /// <summary>BDS の配布 zip の URL。<see cref="BedrockZipPath"/> が指定されていればそちらを優先。</summary>
    public string BedrockDownloadUrl { get; set; } = string.Empty;
    /// <summary>手元にダウンロード済みの BDS zip のパス。</summary>
    public string BedrockZipPath { get; set; } = string.Empty;
    public int PortV6 { get; set; } = 19133;
    public string GameMode { get; set; } = "survival";
    public string Difficulty { get; set; } = "easy";
    public bool AllowCheats { get; set; }
    /// <summary>"raknet" | "nethernet"。transport キーを持つ BDS (1.26.51 以降) のときだけ反映する。</summary>
    public string BedrockTransport { get; set; } = "raknet";
}
