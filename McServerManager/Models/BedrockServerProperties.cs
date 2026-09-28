namespace McServerManager.Models;

/// <summary>統合版 (BDS) の server.properties。キーは Java 版と一部異なる。</summary>
public sealed class BedrockServerProperties
{
    public string ServerName { get; set; } = "Dedicated Server";
    public string GameMode { get; set; } = "survival";
    public bool ForceGameMode { get; set; }
    public string Difficulty { get; set; } = "easy";
    public bool AllowCheats { get; set; }
    public int MaxPlayers { get; set; } = 10;
    public bool OnlineMode { get; set; } = true;
    public bool AllowList { get; set; }
    public int ServerPort { get; set; } = 19132;
    public int ServerPortV6 { get; set; } = 19133;
    /// <summary>
    /// 通信方式 "nethernet" | "raknet"。空はファイルにキーがない（1.26.51 より前の BDS か未設定）ことを表す。
    /// </summary>
    public string Transport { get; set; } = string.Empty;
    /// <summary>NetherNet のゲーム通信用 UDP ポート。書式は "[ip:]external[-external]:internal[-internal]"。</summary>
    public string ServerUdpPorts { get; set; } = string.Empty;
    /// <summary>NetherNet で待ち受ける IP（空なら全インターフェイス）。</summary>
    public string ServerIp { get; set; } = string.Empty;
    public bool EnableLanVisibility { get; set; } = true;
    public int ViewDistance { get; set; } = 32;
    public int TickDistance { get; set; } = 4;
    public int PlayerIdleTimeout { get; set; } = 30;
    public string LevelName { get; set; } = "Bedrock level";
    public string LevelSeed { get; set; } = string.Empty;
    public string DefaultPlayerPermissionLevel { get; set; } = "member";
    public bool TexturePackRequired { get; set; }
}
