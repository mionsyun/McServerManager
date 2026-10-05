using McServerManager.Models;
using McServerManager.Services.Authoring;

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

        // Capture the selected runtime before callbacks or asynchronous installation can
        // mutate options. The declaration must describe the same request we installed.
        var serverType = options.Type;
        var minecraftVersion = options.Version;
        var serverId = Guid.NewGuid().ToString("N");
        var baseDirectory = string.IsNullOrWhiteSpace(options.DirectoryPath)
            ? _pathsService.ServersPath
            : options.DirectoryPath;
        var serverDirectory = Path.Combine(baseDirectory, serverId);

        progress?.Report("作成先フォルダを準備しています...");

        Directory.CreateDirectory(baseDirectory);
        Directory.CreateDirectory(serverDirectory);
        Directory.CreateDirectory(Path.Combine(serverDirectory, "logs"));

        if (ServerEditions.IsBedrock(serverType))
        {
            return await CreateBedrockAsync(options, minecraftVersion, serverId, serverDirectory, progress).ConfigureAwait(false);
        }

        var jarPath = Path.Combine(serverDirectory, "server.jar");
        await _jarService.DownloadAsync(serverType, minecraftVersion, jarPath, options.JavaPath, progress).ConfigureAwait(false);

        progress?.Report("設定ファイルを書き込み中...");
        File.WriteAllText(Path.Combine(serverDirectory, "eula.txt"), "eula=true");

        var config = new ServerConfig
        {
            ServerId = serverId,
            Name = options.Name,
            Type = serverType,
            Version = minecraftVersion,
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
        config.CreationRuntimeDeclaration = ServerCreationRuntimeDeclarationPolicy.Create(serverId, serverType, minecraftVersion);
        _configService.SaveToDirectory(config, serverDirectory);

        progress?.Report("サーバー作成が完了しました。");

        return config;
    }

    private async Task<ServerConfig> CreateBedrockAsync(
        NewServerOptions options,
        string minecraftVersion,
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
            Version = minecraftVersion,
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
        // BDS 同梱の既定は allow-list=true（allowlist.json に登録した人しか入れない）。
        // 作成直後に友達が「招待されていません」と弾かれないよう、Java 版と同じく無効で始める
        props.AllowList = false;
        props.ServerPort = config.Port;
        props.ServerPortV6 = config.PortV6;
        props.LevelName = config.WorldName;
        // 1.26.51 以降の BDS だけが transport を持つ。旧版は RakNet 固定なのでキーを増やさない
        if (!string.IsNullOrWhiteSpace(props.Transport) || BedrockNetworkPlanner.UsesNetherNet(props, config.Version))
        {
            props.Transport = options.BedrockTransport;
        }
        _bedrockPropertiesService.Save(serverDirectory, props);
        _configService.SaveToDirectory(config, serverDirectory);

        progress?.Report("サーバー作成が完了しました。");
        return config;
    }
}
