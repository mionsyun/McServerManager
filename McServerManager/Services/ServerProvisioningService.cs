using McServerManager.Models;

namespace McServerManager.Services;

public sealed class ServerProvisioningService
{
    private readonly AppPathsService _pathsService;
    private readonly ServerPropertiesService _propertiesService;
    private readonly ServerConfigService _configService;
    private readonly ServerJarService _jarService;

    public ServerProvisioningService(
        AppPathsService pathsService,
        ServerPropertiesService propertiesService,
        ServerConfigService configService,
        ServerJarService jarService)
    {
        _pathsService = pathsService;
        _propertiesService = propertiesService;
        _configService = configService;
        _jarService = jarService;
    }

    public async Task<ServerConfig> CreateAsync(NewServerOptions options)
    {
        if (!options.EulaAccepted)
        {
            throw new InvalidOperationException("EULAに同意してください。");
        }

        var serverId = Guid.NewGuid().ToString("N");
        var baseDirectory = string.IsNullOrWhiteSpace(options.DirectoryPath)
            ? _pathsService.ServersPath
            : options.DirectoryPath;
        var serverDirectory = Path.Combine(baseDirectory, serverId);

        Directory.CreateDirectory(baseDirectory);
        Directory.CreateDirectory(serverDirectory);
        Directory.CreateDirectory(Path.Combine(serverDirectory, "logs"));
        Directory.CreateDirectory(Path.Combine(serverDirectory, "backups"));

        var jarPath = Path.Combine(serverDirectory, "server.jar");
        await _jarService.DownloadAsync(options.Type, options.Version, jarPath, options.JavaPath).ConfigureAwait(false);

        File.WriteAllText(Path.Combine(serverDirectory, "eula.txt"), "eula=true");

        var config = new ServerConfig
        {
            ServerId = serverId,
            Name = options.Name,
            Type = options.Type,
            Version = options.Version,
            DirectoryPath = serverDirectory,
            JavaPath = options.JavaPath,
            MemoryXmsMb = options.MemoryXmsMb,
            MemoryXmxMb = options.MemoryXmxMb,
            Port = options.Port,
            MaxPlayers = options.MaxPlayers,
            OnlineMode = options.OnlineMode,
            Motd = options.Motd,
            WorldName = "world",
            Difficulty = "easy",
            GameMode = "survival",
            Pvp = true,
            ViewDistance = 10,
            SpawnProtection = 16,
            AutoRestartOnCrash = true,
            AutoRestartDelaySeconds = 10,
            CreatedAt = DateTime.UtcNow
        };

        var props = new ServerProperties
        {
            ServerPort = config.Port,
            MaxPlayers = config.MaxPlayers,
            Motd = config.Motd,
            OnlineMode = config.OnlineMode,
            Difficulty = config.Difficulty,
            GameMode = config.GameMode,
            Pvp = config.Pvp,
            ViewDistance = config.ViewDistance,
            SpawnProtection = config.SpawnProtection,
            LevelName = config.WorldName,
            Seed = config.Seed
        };

        _propertiesService.Save(serverDirectory, props);
        _configService.SaveToDirectory(config, serverDirectory);

        return config;
    }
}
