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
    public bool EulaAccepted { get; set; }
    public string JavaPath { get; set; } = string.Empty;
    public string Motd { get; set; } = "A Minecraft Server";
}
