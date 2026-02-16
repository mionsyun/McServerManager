namespace McServerManager.Models;

public sealed class ServerConfig
{
    public string ServerId { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Type { get; set; } = "Vanilla";
    public string Version { get; set; } = "latest";
    public string DirectoryPath { get; set; } = string.Empty;
    public string JavaPath { get; set; } = string.Empty;
    public int MemoryXmsMb { get; set; } = 1024;
    public int MemoryXmxMb { get; set; } = 2048;
    public int Port { get; set; } = 25565;
    public int MaxPlayers { get; set; } = 20;
    public bool OnlineMode { get; set; } = true;
    public string Motd { get; set; } = "A Minecraft Server";
    public string WorldName { get; set; } = "world";
    public string Seed { get; set; } = string.Empty;
    public string Difficulty { get; set; } = "easy";
    public string GameMode { get; set; } = "survival";
    public bool Pvp { get; set; } = true;
    public int ViewDistance { get; set; } = 10;
    public int SpawnProtection { get; set; } = 16;
    public bool AutoRestartOnCrash { get; set; } = true;
    public int AutoRestartDelaySeconds { get; set; } = 10;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? LastStartedAt { get; set; }
    public FirewallRuleInfo Firewall { get; set; } = new();
}
