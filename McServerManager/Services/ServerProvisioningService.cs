using McServerManager.Models;

namespace McServerManager.Services;

public sealed class ServerProvisioningService : IServerProvisioningService
{
    private readonly AppPathsService _pathsService;
    private readonly IServerPropertiesService _propertiesService;
    private readonly IServerConfigService _configService;
    private readonly IServerJarService _jarService;
    private readonly IBedrockServerService _bedrockServerService;
    private readonly IBedrockPropertiesService _bedrockPropertiesService;

    public ServerProvisioningService(
        AppPathsService pathsService,
        IServerPropertiesService propertiesService,
        IServerConfigService configService,
        IServerJarService jarService,
        IBedrockServerService bedrockServerService,
        IBedrockPropertiesService bedrockPropertiesService)
    {
        _pathsService = pathsService;
        _propertiesService = propertiesService;
        _configService = configService;
        _jarService = jarService;
        _bedrockServerService = bedrockServerService;
        _bedrockPropertiesService = bedrockPropertiesService;
    }

    public async Task<ServerConfig> CreateAsync(NewServerOptions options, IProgress<string>? progress = null)
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

        progress?.Report("作成先フォルダを準備しています...");

        Directory.CreateDirectory(baseDirectory);
        Directory.CreateDirectory(serverDirectory);
        Directory.CreateDirectory(Path.Combine(serverDirectory, "logs"));

        if (ServerEditions.IsBedrock(options.Type))
        {
            return await CreateBedrockAsync(options, serverId, serverDirectory, progress).ConfigureAwait(false);
        }

        var jarPath = Path.Combine(serverDirectory, "server.jar");
        await _jarService.DownloadAsync(options.Type, options.Version, jarPath, options.JavaPath, progress).ConfigureAwait(false);

        progress?.Report("設定ファイルを書き込み中...");
        File.WriteAllText(Path.Combine(serverDirectory, "eula.txt"), "eula=true");

        var config = new ServerConfig
        {
            ServerId = serverId,
            Name = options.Name,
            Type = options.Type,
            Version = options.Version,
            DirectoryPath = serverDirectory,
            JavaPath = options.JavaPath,
            JavaExtraArguments = string.Empty,
            LaunchModeOverride = "Auto",
            StartupPresetId = "Balanced",
            MemoryXmsMb = options.MemoryXmsMb,
            MemoryXmxMb = options.MemoryXmxMb,
            Port = options.Port,
            MaxPlayers = options.MaxPlayers,
            OnlineMode = options.OnlineMode,
            EnableCommandBlock = options.EnableCommandBlock,
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
            EnableCommandBlock = config.EnableCommandBlock,
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

        progress?.Report("サーバー作成が完了しました。");

        return config;
    }

    private async Task<ServerConfig> CreateBedrockAsync(
        NewServerOptions options,
        string serverId,
        string serverDirectory,
        IProgress<string>? progress)
    {
        if (!string.IsNullOrWhiteSpace(options.BedrockZipPath))
        {
            await _bedrockServerService
                .InstallFromZipAsync(options.BedrockZipPath, serverDirectory, progress)
                .ConfigureAwait(false);
        }
        else
        {
            await _bedrockServerService
                .InstallFromUrlAsync(options.BedrockDownloadUrl, serverDirectory, progress)
                .ConfigureAwait(false);
        }

        progress?.Report("設定ファイルを書き込み中...");
        Directory.CreateDirectory(Path.Combine(serverDirectory, ServerEditions.BedrockWorldsDirectoryName));

        var config = new ServerConfig
        {
            ServerId = serverId,
            Name = options.Name,
            Type = ServerEditions.BedrockType,
            Version = options.Version,
            DirectoryPath = serverDirectory,
            LaunchModeOverride = "Auto",
            Port = options.Port,
            PortV6 = options.PortV6,
            MaxPlayers = options.MaxPlayers,
            OnlineMode = options.OnlineMode,
            EnableCommandBlock = options.AllowCheats,
            Motd = options.Motd,
            WorldName = ServerEditions.BedrockDefaultLevelName,
            Difficulty = options.Difficulty,
            GameMode = options.GameMode,
            AutoRestartOnCrash = true,
            AutoRestartDelaySeconds = 10,
            CreatedAt = DateTime.UtcNow
        };

        // zip 同梱の server.properties（説明コメント付き）を土台に、作成時の選択だけ上書きする
        var props = _bedrockPropertiesService.Load(serverDirectory);
        props.ServerName = config.Motd;
        props.GameMode = config.GameMode;
        props.Difficulty = config.Difficulty;
        props.AllowCheats = options.AllowCheats;
        props.MaxPlayers = config.MaxPlayers;
        props.OnlineMode = config.OnlineMode;
        props.ServerPort = config.Port;
        props.ServerPortV6 = config.PortV6;
        props.LevelName = config.WorldName;
        _bedrockPropertiesService.Save(serverDirectory, props);
        _configService.SaveToDirectory(config, serverDirectory);

        progress?.Report("サーバー作成が完了しました。");
        return config;
    }
}
