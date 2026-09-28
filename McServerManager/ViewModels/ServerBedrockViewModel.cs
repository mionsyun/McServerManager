using System.Windows;
using McServerManager.Models;
using McServerManager.Services;
using McServerManager.Utilities;

namespace McServerManager.ViewModels;

/// <summary>
/// 統合版 (BDS) サーバー専用の画面ロジック: server.properties 編集・プレイヤー管理・接続状況・BDS の更新。
/// Java 版サーバーでは生成しない。
/// </summary>
public sealed class ServerBedrockViewModel : ObservableObject
{
    private readonly IBedrockPropertiesService _properties;
    private readonly IServerConfigService _configs;
    private readonly IDialogService _dialog;
    private readonly INetworkService _network;
    private readonly ServerConfig _config;

    private string _signalingStatus = "未確認（サーバー起動後に表示）";
    private bool _isSignalingSignedIn;

    public ServerBedrockViewModel(
        IBedrockServerService bedrockServer,
        IBedrockPropertiesService properties,
        IWorldService worlds,
        IServerConfigService configs,
        IDialogService dialog,
        INetworkService network,
        ServerConfig config,
        Func<ServerStatus> getStatus,
        Action<string> sendCommand)
    {
        _properties = properties;
        _configs = configs;
        _dialog = dialog;
        _network = network;
        _config = config;

        Settings = new BedrockSettingsViewModel();
        Settings.PropertyChanged += (_, _) => OnPropertyChanged(nameof(NetworkWarnings));
        Players = new BedrockPlayersViewModel(properties, config.DirectoryPath, () => getStatus() == ServerStatus.Running, sendCommand);

        Update = new BedrockUpdateViewModel(bedrockServer, worlds, configs, dialog, config, getStatus);
        AutoConfigureUdpPortsCommand = new AsyncRelayCommand(AutoConfigureUdpPortsAsync);

        LoadSettings();
        Players.Load();
    }

    /// <summary>設定保存後に ServerViewModel へ通知（ポート表示などの更新用）。</summary>
    public event Action? SettingsSaved;

    public BedrockSettingsViewModel Settings { get; }
    public BedrockPlayersViewModel Players { get; }
    public BedrockUpdateViewModel Update { get; }

    public AsyncRelayCommand AutoConfigureUdpPortsCommand { get; }

    /// <summary>編集中の設定で外部公開を妨げそうな点（保存前から表示する）。</summary>
    public IReadOnlyList<string> NetworkWarnings =>
        BedrockNetworkPlanner.GetWarnings(Settings.ToModel(), _config.Version);

    /// <summary>NetherNet のシグナリングサービス (Microsoft, TLS 443) へのサインイン状況。起動ログから判定する。</summary>
    public string SignalingStatus
    {
        get => _signalingStatus;
        private set => SetProperty(ref _signalingStatus, value);
    }

    public bool IsSignalingSignedIn
    {
        get => _isSignalingSignedIn;
        private set => SetProperty(ref _isSignalingSignedIn, value);
    }

    /// <summary>サーバーログ 1 行ごとに ServerViewModel から呼ばれる（バックグラウンドスレッド）。</summary>
    public void HandleLog(string line)
    {
        if (line.Contains("Signed in to signaling service successfully", StringComparison.OrdinalIgnoreCase))
        {
            IsSignalingSignedIn = true;
            SignalingStatus = "✓ サインイン済み（Signed in to signaling service successfully）";
        }
        else if (line.Contains("NetherNet is the only supported transport type", StringComparison.OrdinalIgnoreCase))
        {
            SignalingStatus = "RakNet で起動中（警告は表示されますが、RakNet が無効になるわけではありません）";
        }
    }

    /// <summary>起動時に ServerViewModel から呼ぶ。</summary>
    public void ResetSignalingStatus()
    {
        IsSignalingSignedIn = false;
        SignalingStatus = "確認中...（起動ログに「Signed in to signaling service successfully」が出れば正常）";
    }

    public void LoadSettings()
    {
        try
        {
            var props = _properties.Load(_config.DirectoryPath);
            // 1.26.51 以降でキーがない（旧版から更新した）場合は、BDS の既定である NetherNet を明示する
            if (string.IsNullOrWhiteSpace(props.Transport) && BedrockNetworkPlanner.UsesNetherNet(props, _config.Version))
                props.Transport = BedrockNetworkPlanner.TransportNetherNet;
            Settings.Load(props);
        }
        catch (Exception ex)
        {
            _dialog.Show($"server.properties の読み込みに失敗しました: {ex.Message}", "エラー", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    /// <summary>server.properties を保存し、アプリ側の設定 (config.json) にも反映する。</summary>
    public bool SaveSettings()
    {
        var error = Settings.Validate();
        if (error is not null)
        {
            _dialog.Show(error, "入力エラー", MessageBoxButton.OK, MessageBoxImage.Warning);
            return false;
        }

        var props = Settings.ToModel();
        _properties.Save(_config.DirectoryPath, props);
        _config.Motd = props.ServerName;
        _config.Port = props.ServerPort;
        _config.PortV6 = props.ServerPortV6;
        _config.MaxPlayers = props.MaxPlayers;
        _config.OnlineMode = props.OnlineMode;
        _config.GameMode = props.GameMode;
        _config.Difficulty = props.Difficulty;
        _config.EnableCommandBlock = props.AllowCheats;
        _config.WorldName = props.LevelName;
        _config.Seed = props.LevelSeed;
        _configs.Save(_config);
        Settings.Load(props);
        SettingsSaved?.Invoke();
        return true;
    }

    /// <summary>
    /// 外部公開用に server-udp-ports を「グローバル IP:範囲:範囲」で入力する（保存は「設定を保存」で行う）。
    /// 範囲は最大人数分。NAT 越しで届く候補はこの設定でしか広告できないため IP を付ける。
    /// </summary>
    private async Task AutoConfigureUdpPortsAsync()
    {
        var ip = await _network.GetPublicIpAsync();
        if (string.IsNullOrWhiteSpace(ip))
        {
            _dialog.Show(
                "グローバル IP を取得できなかったため、IP なしで入力します。外部公開する場合は先頭にグローバル IP を追記してください。",
                "確認", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        Settings.ServerUdpPorts = BedrockNetworkPlanner.BuildUdpPortsValue(
            ip, BedrockNetworkPlanner.RecommendedUdpPortStart, Math.Max(Settings.MaxPlayers, 2));
        if (!string.IsNullOrWhiteSpace(ip))
        {
            _dialog.Show(
                $"ゲーム通信 UDP ポートを「{Settings.ServerUdpPorts}」に設定しました。\n「設定を保存」→ 再起動で反映されます。\n\nグローバル IP が変わる回線では、変わるたびに設定し直す必要があります。",
                "設定しました", MessageBoxButton.OK, MessageBoxImage.Information);
        }
    }

}
