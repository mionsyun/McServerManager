using McServerManager.Models;
using McServerManager.Services;
using McServerManager.Utilities;

namespace McServerManager.ViewModels;

/// <summary>統合版 (BDS) の server.properties 編集フォーム。変更検知 (IsDirty) を持つ。</summary>
public sealed class BedrockSettingsViewModel : ObservableObject
{
    private BedrockServerProperties _original = new();
    private string _serverName = string.Empty;
    private string _gameMode = "survival";
    private bool _forceGameMode;
    private string _difficulty = "easy";
    private bool _allowCheats;
    private int _maxPlayers;
    private bool _onlineMode;
    private bool _allowList;
    private int _serverPort;
    private int _serverPortV6;
    private string _transport = string.Empty;
    private string _serverUdpPorts = string.Empty;
    private bool _enableLanVisibility;
    private int _viewDistance;
    private int _tickDistance;
    private int _playerIdleTimeout;
    private string _levelName = string.Empty;
    private string _levelSeed = string.Empty;
    private string _defaultPlayerPermissionLevel = "member";
    private bool _texturePackRequired;
    private bool _isDirty;

    public static IReadOnlyList<ChoiceOption> GameModes { get; } =
    [
        new("survival", "サバイバル"),
        new("creative", "クリエイティブ"),
        new("adventure", "アドベンチャー"),
    ];

    public static IReadOnlyList<ChoiceOption> Difficulties { get; } =
    [
        new("peaceful", "ピースフル"),
        new("easy", "イージー"),
        new("normal", "ノーマル"),
        new("hard", "ハード"),
    ];

    /// <summary>空 = server.properties にキーがない（古い BDS）。その場合は選択肢を出さない。</summary>
    public static IReadOnlyList<ChoiceOption> Transports { get; } =
    [
        new(BedrockNetworkPlanner.TransportNetherNet, "NetherNet（標準・推奨）"),
        new(BedrockNetworkPlanner.TransportRakNet, "RakNet（旧方式・接続できないときの回避用）"),
    ];

    public static IReadOnlyList<ChoiceOption> PermissionLevels { get; } =
    [
        new("visitor", "訪問者（見るだけ）"),
        new("member", "メンバー（通常）"),
        new("operator", "オペレーター（全員 OP）"),
    ];

    public string ServerName { get => _serverName; set => Set(ref _serverName, value); }
    public string GameMode { get => _gameMode; set => Set(ref _gameMode, value); }
    public bool ForceGameMode { get => _forceGameMode; set => Set(ref _forceGameMode, value); }
    public string Difficulty { get => _difficulty; set => Set(ref _difficulty, value); }
    public bool AllowCheats { get => _allowCheats; set => Set(ref _allowCheats, value); }
    public int MaxPlayers { get => _maxPlayers; set => Set(ref _maxPlayers, value); }
    public bool OnlineMode { get => _onlineMode; set => Set(ref _onlineMode, value); }
    public bool AllowList { get => _allowList; set => Set(ref _allowList, value); }
    public int ServerPort { get => _serverPort; set => Set(ref _serverPort, value); }
    public int ServerPortV6 { get => _serverPortV6; set => Set(ref _serverPortV6, value); }

    public string Transport
    {
        get => _transport;
        set
        {
            Set(ref _transport, value);
            OnPropertyChanged(nameof(IsNetherNetSelected));
            OnPropertyChanged(nameof(IsRakNetSelected));
        }
    }

    /// <summary>server.properties に transport キーがある（BDS 1.26.51 以降）か。</summary>
    public bool SupportsTransport => !string.IsNullOrWhiteSpace(_original.Transport);
    public bool IsNetherNetSelected => string.Equals(Transport, BedrockNetworkPlanner.TransportNetherNet, StringComparison.OrdinalIgnoreCase);
    public bool IsRakNetSelected => string.Equals(Transport, BedrockNetworkPlanner.TransportRakNet, StringComparison.OrdinalIgnoreCase);

    public string ServerUdpPorts { get => _serverUdpPorts; set => Set(ref _serverUdpPorts, value); }
    public bool EnableLanVisibility { get => _enableLanVisibility; set => Set(ref _enableLanVisibility, value); }
    public int ViewDistance { get => _viewDistance; set => Set(ref _viewDistance, value); }
    public int TickDistance { get => _tickDistance; set => Set(ref _tickDistance, value); }
    public int PlayerIdleTimeout { get => _playerIdleTimeout; set => Set(ref _playerIdleTimeout, value); }
    public string LevelName { get => _levelName; set => Set(ref _levelName, value); }
    public string LevelSeed { get => _levelSeed; set => Set(ref _levelSeed, value); }
    public string DefaultPlayerPermissionLevel { get => _defaultPlayerPermissionLevel; set => Set(ref _defaultPlayerPermissionLevel, value); }
    public bool TexturePackRequired { get => _texturePackRequired; set => Set(ref _texturePackRequired, value); }

    public bool IsDirty
    {
        get => _isDirty;
        private set => SetProperty(ref _isDirty, value);
    }

    public void Load(BedrockServerProperties properties)
    {
        _original = properties;
        ServerName = properties.ServerName;
        GameMode = properties.GameMode;
        ForceGameMode = properties.ForceGameMode;
        Difficulty = properties.Difficulty;
        AllowCheats = properties.AllowCheats;
        MaxPlayers = properties.MaxPlayers;
        OnlineMode = properties.OnlineMode;
        AllowList = properties.AllowList;
        ServerPort = properties.ServerPort;
        ServerPortV6 = properties.ServerPortV6;
        Transport = properties.Transport;
        ServerUdpPorts = properties.ServerUdpPorts;
        EnableLanVisibility = properties.EnableLanVisibility;
        ViewDistance = properties.ViewDistance;
        TickDistance = properties.TickDistance;
        PlayerIdleTimeout = properties.PlayerIdleTimeout;
        LevelName = properties.LevelName;
        LevelSeed = properties.LevelSeed;
        DefaultPlayerPermissionLevel = properties.DefaultPlayerPermissionLevel;
        TexturePackRequired = properties.TexturePackRequired;
        IsDirty = false;
        OnPropertyChanged(nameof(SupportsTransport));
    }

    public BedrockServerProperties ToModel() => new()
    {
        ServerName = ServerName.Trim(),
        GameMode = GameMode,
        ForceGameMode = ForceGameMode,
        Difficulty = Difficulty,
        AllowCheats = AllowCheats,
        MaxPlayers = MaxPlayers,
        OnlineMode = OnlineMode,
        AllowList = AllowList,
        ServerPort = ServerPort,
        ServerPortV6 = ServerPortV6,
        Transport = Transport,
        ServerUdpPorts = ServerUdpPorts.Trim(),
        ServerIp = _original.ServerIp,
        EnableLanVisibility = EnableLanVisibility,
        ViewDistance = ViewDistance,
        TickDistance = TickDistance,
        PlayerIdleTimeout = PlayerIdleTimeout,
        LevelName = LevelName.Trim(),
        LevelSeed = LevelSeed.Trim(),
        DefaultPlayerPermissionLevel = DefaultPlayerPermissionLevel,
        TexturePackRequired = TexturePackRequired
    };

    /// <summary>保存前の入力チェック。問題があればユーザー向けメッセージを返す。</summary>
    public string? Validate()
    {
        if (string.IsNullOrWhiteSpace(ServerName))
            return "サーバー名を入力してください。";
        if (ServerName.Contains(';'))
            return "サーバー名にセミコロン (;) は使えません。";
        if (ServerPort is < 1 or > 65535 || ServerPortV6 is < 1 or > 65535)
            return "ポート番号は 1〜65535 で入力してください。";
        if (ServerPort == ServerPortV6)
            return "IPv4 と IPv6 のポートは別の番号にしてください。";
        if (!string.IsNullOrWhiteSpace(ServerUdpPorts)
            && !BedrockNetworkPlanner.TryParseUdpPorts(ServerUdpPorts, out _, out var udpPortsError))
            return udpPortsError ?? "ゲーム通信 UDP ポートの書式が正しくありません。";
        if (MaxPlayers is < 1 or > 200)
            return "最大人数は 1〜200 で入力してください。";
        if (ViewDistance < 5)
            return "描画距離は 5 以上で入力してください。";
        if (TickDistance is < 4 or > 12)
            return "シミュレーション距離は 4〜12 で入力してください。";
        if (string.IsNullOrWhiteSpace(LevelName))
            return "ワールド名を入力してください。";
        return null;
    }

    private void Set<T>(ref T field, T value, [System.Runtime.CompilerServices.CallerMemberName] string? propertyName = null)
    {
        if (SetProperty(ref field, value, propertyName))
        {
            UpdateDirty();
        }
    }

    private void UpdateDirty()
    {
        var current = ToModel();
        IsDirty =
            _original.ServerName != current.ServerName ||
            _original.GameMode != current.GameMode ||
            _original.ForceGameMode != current.ForceGameMode ||
            _original.Difficulty != current.Difficulty ||
            _original.AllowCheats != current.AllowCheats ||
            _original.MaxPlayers != current.MaxPlayers ||
            _original.OnlineMode != current.OnlineMode ||
            _original.AllowList != current.AllowList ||
            _original.ServerPort != current.ServerPort ||
            _original.ServerPortV6 != current.ServerPortV6 ||
            _original.Transport != current.Transport ||
            _original.ServerUdpPorts != current.ServerUdpPorts ||
            _original.EnableLanVisibility != current.EnableLanVisibility ||
            _original.ViewDistance != current.ViewDistance ||
            _original.TickDistance != current.TickDistance ||
            _original.PlayerIdleTimeout != current.PlayerIdleTimeout ||
            _original.LevelName != current.LevelName ||
            _original.LevelSeed != current.LevelSeed ||
            _original.DefaultPlayerPermissionLevel != current.DefaultPlayerPermissionLevel ||
            _original.TexturePackRequired != current.TexturePackRequired;
    }
}

/// <summary>ComboBox 用の値と表示名の組。</summary>
public sealed record ChoiceOption(string Id, string Label);
