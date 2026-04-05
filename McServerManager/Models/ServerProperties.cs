namespace McServerManager.Models;

public sealed class ServerProperties
{
    public int ServerPort { get; set; } = 25565;
    public int MaxPlayers { get; set; } = 20;
    public string Motd { get; set; } = "A Minecraft Server";
    public bool OnlineMode { get; set; } = true;
    public bool EnableCommandBlock { get; set; }
    public string Difficulty { get; set; } = "easy";
    public string GameMode { get; set; } = "survival";
    public bool Pvp { get; set; } = true;
    public int ViewDistance { get; set; } = 10;
    public int SpawnProtection { get; set; } = 16;
    public string LevelName { get; set; } = "world";
    public string Seed { get; set; } = string.Empty;
    public string ResourcePack { get; set; } = string.Empty;
    public string ResourcePackSha1 { get; set; } = string.Empty;
}
