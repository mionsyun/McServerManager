namespace McServerManager.Models;

public sealed class ServerConfig
{
    public string ServerId { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Type { get; set; } = "Vanilla";
    public string Version { get; set; } = "latest";
    public string DirectoryPath { get; set; } = string.Empty;
    public string JavaPath { get; set; } = string.Empty;
    public string JavaExtraArguments { get; set; } = string.Empty;
    public string LaunchModeOverride { get; set; } = "Auto";
    public string StartupPresetId { get; set; } = "Balanced";
    public int MemoryXmsMb { get; set; } = 1024;
    public int MemoryXmxMb { get; set; } = 2048;
    public int Port { get; set; } = 25565;
    public int MaxPlayers { get; set; } = 20;
    public bool OnlineMode { get; set; } = true;
    public bool EnableCommandBlock { get; set; }
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
    public bool ScheduledBackupEnabled { get; set; }
    public int ScheduledBackupIntervalMinutes { get; set; } = 60;
    public int ScheduledBackupMaxRetention { get; set; } = 10;
    public DateTime? LastScheduledBackupAt { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? LastStartedAt { get; set; }
    public bool HasCompletedInitialHealthCheck { get; set; }
    public FirewallRuleInfo Firewall { get; set; } = new();
}
