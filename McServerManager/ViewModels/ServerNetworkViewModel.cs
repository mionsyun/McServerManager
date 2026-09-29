using System.Collections.ObjectModel;
using System.Windows;
using McServerManager.Models;
using McServerManager.Services;
using McServerManager.Utilities;

namespace McServerManager.ViewModels;

public sealed class ServerNetworkViewModel : ObservableObject
{
    private readonly AppServices _services;
    private readonly ServerConfig _config;
    private readonly AppSettings _appSettings;
    private readonly IPortForwardingService _portForwarding;

    private string _publicIp = "-";
    private string _publicIpStatus = string.Empty;
    private bool _upnpOpened;
    private bool _usesNetherNet;

    public ServerNetworkViewModel(
        AppServices services,
        ServerConfig config,
        AppSettings appSettings,
        IPortForwardingService portForwarding)
    {
        _services = services;
        _config = config;
        _appSettings = appSettings;
        _portForwarding = portForwarding;

        LanIpAddresses = new ObservableCollection<string>(_services.Network.GetLanIpAddresses());
        RequiredPorts = [];
        BedrockWarnings = [];
        RefreshPortPlan();
        ExternalChecklist = new ObservableCollection<string>(_services.Network.GetExternalChecklist(PrimaryProtocol));

        CreateFirewallRuleCommand = new RelayCommand(_ => ApplyFirewallRule(recreate: false));
        DeleteFirewallRuleCommand = new RelayCommand(_ => DeleteFirewallRule());
        RecreateFirewallRuleCommand = new RelayCommand(_ => ApplyFirewallRule(recreate: true));
        OpenPortCommand = new AsyncRelayCommand(OpenPortAsync);
        ClosePortCommand = new AsyncRelayCommand(ClosePortAsync);
        RefreshPublicIpCommand = new AsyncRelayCommand(RefreshPublicIpAsync);
        CopyShareAddressCommand = new RelayCommand(_ => CopyShareAddress());
    }

    public ObservableCollection<string> LanIpAddresses { get; }
    public ObservableCollection<string> ExternalChecklist { get; }

    /// <summary>ルーターで開放が必要なポート。Java 版は TCP 1 つ、統合版は通信方式で変わる。</summary>
    public ObservableCollection<PortRequirement> RequiredPorts { get; }

    /// <summary>統合版の外部公開を妨げそうな設定の注意点。</summary>
    public ObservableCollection<string> BedrockWarnings { get; }

    public bool HasBedrockWarnings => BedrockWarnings.Count > 0;

    public string PublicIp
    {
        get => _publicIp;
        private set => SetProperty(ref _publicIp, value);
    }

    public string PublicIpStatus
    {
        get => _publicIpStatus;
        private set => SetProperty(ref _publicIpStatus, value);
    }

    /// <summary>Firewall 受信ルールが構成済みか(ルール名が登録されているか)。</summary>
    public bool IsFirewallConfigured => !string.IsNullOrWhiteSpace(_config.Firewall.TcpRuleName);

    /// <summary>このセッションで UPnP ポート開放を行ったか。</summary>
    public bool IsUpnpOpen => _upnpOpened;

    public RelayCommand CreateFirewallRuleCommand { get; }
    public RelayCommand DeleteFirewallRuleCommand { get; }
    public RelayCommand RecreateFirewallRuleCommand { get; }
    public AsyncRelayCommand OpenPortCommand { get; }
    public AsyncRelayCommand ClosePortCommand { get; }
    public AsyncRelayCommand RefreshPublicIpCommand { get; }
    public RelayCommand CopyShareAddressCommand { get; }

    private int Port => _config.Port;

    public bool IsBedrock => ServerEditions.IsBedrock(_config);

    /// <summary>統合版が NetherNet (TCP シグナリング + UDP ゲーム通信) で動くか。false なら RakNet (UDP)。</summary>
    public bool UsesNetherNet => _usesNetherNet;

    public string TransportLabel => !IsBedrock ? "Java" : UsesNetherNet ? "NetherNet" : "RakNet";

    /// <summary>server-port を待ち受けるプロトコル。使用中チェックに使う。</summary>
    public NetworkProtocol PrimaryProtocol =>
        IsBedrock && !UsesNetherNet ? NetworkProtocol.Udp : NetworkProtocol.Tcp;

    public string ProtocolLabel => !IsBedrock ? "TCP" : UsesNetherNet ? "TCP+UDP" : "UDP";

    /// <summary>画面表示用（例: "TCP 25565" / "TCP 19132 + UDP 19300-19319"）。</summary>
    public string PortLabel
    {
        get
        {
            var label = string.Join(" + ", RequiredPorts.Select(p => p.Label));
            var hasUdp = RequiredPorts.Any(p => p.Protocol == NetworkProtocol.Udp);
            return UsesNetherNet && !hasUdp ? $"{label} + UDP (未固定)" : label;
        }
    }

    /// <summary>ポート・通信方式の設定変更後に ServerViewModel から呼ぶ。</summary>
    public void NotifyPortChanged() => RefreshPortPlan();

    /// <summary>サーバー起動時にルーターのポートを UPnP で自動開放する（アプリ全体の設定）。</summary>
    public bool AutoOpenUpnpOnStart
    {
        get
        {
            // 別サーバーの画面で変更された場合もあるため、保存済みの値を読む
            var settings = _services.Settings.Load();
            return settings.EnableUpnp && !settings.PromptUpnp;
        }
        set
        {
            SaveUpnpChoice(value);
            OnPropertyChanged();
        }
    }

    private void SaveUpnpChoice(bool enable)
    {
        // 他のサーバーの画面が持つ設定も古くならないよう、ファイルの最新値を部分更新する
        var saved = _services.Settings.Update(s =>
        {
            s.EnableUpnp = enable;
            s.PromptUpnp = false;
        });
        _appSettings.EnableUpnp = saved.EnableUpnp;
        _appSettings.PromptUpnp = saved.PromptUpnp;
    }

    /// <summary>サーバー起動時にServerViewModelから呼び出す。初回だけ確認し、答えを記憶する。</summary>
    public async Task TryOpenPortAsync()
    {
        var settings = _services.Settings.Load();
        if (settings.PromptUpnp)
        {
            var result = _services.Dialog.Show(
                "ルーターのポートを UPnP で自動開放しますか？\n\n" +
                "「はい」: サーバーを起動するたびに自動で開放します\n" +
                "「いいえ」: 開放しません（同じ Wi-Fi 内の人はそのまま参加できます）\n\n" +
                "この選択は記憶され、「接続・公開」画面でいつでも変更できます。",
                "ポートの自動開放", MessageBoxButton.YesNo, MessageBoxImage.Question);
            SaveUpnpChoice(result == MessageBoxResult.Yes);
            OnPropertyChanged(nameof(AutoOpenUpnpOnStart));
            if (result != MessageBoxResult.Yes)
                return;
        }
        else if (!settings.EnableUpnp)
        {
            return;
        }

        var error = await OpenRequiredPortsAsync();
        if (error is not null)
        {
            _services.Dialog.Show(error, "警告", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    /// <summary>サーバー停止時にServerViewModelから呼び出す。</summary>
    public async Task TryClosePortAsync()
    {
        if (!_upnpOpened)
            return;
        await CloseRequiredPortsAsync();
    }

    /// <summary>サーバー起動前にServerViewModelから呼び出す。</summary>
    public bool EnsurePortAvailable()
    {
        var protocol = PrimaryProtocol;
        var processes = _services.Network.GetProcessesUsingPort(Port, protocol);
        if (processes.Count == 0)
            return true;

        var names = string.Join(", ", processes.Select(p => $"{p.ProcessName}({p.Id})"));
        var protocolLabel = protocol == NetworkProtocol.Udp ? "UDP" : "TCP";
        var result = _services.Dialog.Show(
            $"{protocolLabel} {Port} を使用しているプロセスがあります: {names}\n終了させて続行しますか？",
            "ポート使用中", MessageBoxButton.YesNo, MessageBoxImage.Warning);

        if (result != MessageBoxResult.Yes)
            return false;

        if (!_services.Network.TryKillProcessesUsingPort(Port, out var error, protocol))
        {
            _services.Dialog.Show(
                $"プロセス終了に失敗しました: {error}",
                "エラー", MessageBoxButton.OK, MessageBoxImage.Error);
            return false;
        }

        return true;
    }

    private void RefreshPortPlan()
    {
        var plan = _portForwarding.GetPlan(_config);
        _usesNetherNet = plan.UsesNetherNet;
        RequiredPorts.Clear();
        foreach (var port in plan.Ports)
            RequiredPorts.Add(port);
        BedrockWarnings.Clear();
        foreach (var warning in plan.Warnings)
            BedrockWarnings.Add(warning);

        OnPropertyChanged(nameof(UsesNetherNet));
        OnPropertyChanged(nameof(TransportLabel));
        OnPropertyChanged(nameof(PrimaryProtocol));
        OnPropertyChanged(nameof(ProtocolLabel));
        OnPropertyChanged(nameof(PortLabel));
        OnPropertyChanged(nameof(HasBedrockWarnings));
    }

    /// <summary>必要なポートを UPnP で開放する。失敗時はユーザー向けメッセージを返す。</summary>
    private async Task<string?> OpenRequiredPortsAsync()
    {
        RefreshPortPlan();
        var error = await _portForwarding.OpenAsync(_config, RequiredPorts.ToList());
        _upnpOpened |= error is null;
        OnPropertyChanged(nameof(IsUpnpOpen));
        return error;
    }

    private async Task CloseRequiredPortsAsync()
    {
        await _portForwarding.CloseAsync(RequiredPorts.ToList());
        _upnpOpened = false;
        OnPropertyChanged(nameof(IsUpnpOpen));
    }

    private void ApplyFirewallRule(bool recreate)
    {
        try
        {
            _services.Firewall.ApplyServerRules(_config, recreate);
            _services.Configs.Save(_config);
            OnPropertyChanged(nameof(IsFirewallConfigured));
            _services.Dialog.Show(
                IsBedrock
                    ? "Firewall ルールを作成しました（bedrock_server.exe の TCP/UDP 受信を許可）。"
                    : $"Firewall ルールを{(recreate ? "再作成" : "作成")}しました。",
                "完了", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            _services.Dialog.Show(
                $"Firewall ルール作成に失敗しました: {ex.Message}",
                "エラー", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void DeleteFirewallRule()
    {
        try
        {
            _services.Firewall.DeleteRules(_config.Firewall);
            OnPropertyChanged(nameof(IsFirewallConfigured));
            _services.Dialog.Show(
                "Firewall ルールを削除しました。",
                "完了", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            _services.Dialog.Show(
                $"Firewall ルール削除に失敗しました: {ex.Message}",
                "エラー", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private async Task OpenPortAsync()
    {
        var error = await OpenRequiredPortsAsync();
        if (error is not null)
        {
            _services.Dialog.Show(error, "警告", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var note = UsesNetherNet && !RequiredPorts.Any(p => p.Protocol == NetworkProtocol.Udp)
            ? "\n\nゲーム通信用の UDP ポートが未設定のため、外部から接続できない可能性があります。サーバー設定の「ゲーム通信 UDP ポート」を設定してください。"
            : string.Empty;
        _services.Dialog.Show(
            $"{PortLabel} のポート開放を実行しました。{note}",
            "完了", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private async Task ClosePortAsync()
    {
        await CloseRequiredPortsAsync();
        _services.Dialog.Show(
            $"{PortLabel} のポート閉鎖を実行しました。",
            "完了", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private async Task RefreshPublicIpAsync()
    {
        PublicIpStatus = "取得中...";
        var ip = await _services.Network.GetPublicIpAsync();
        PublicIp = string.IsNullOrWhiteSpace(ip) ? "-" : ip;
        PublicIpStatus = string.IsNullOrWhiteSpace(ip) ? "取得失敗" : string.Empty;
    }

    private void CopyShareAddress()
    {
        try
        {
            System.Windows.Clipboard.SetText(PublicIp);
        }
        catch
        {
            // Clipboard may fail in rare cases
        }
    }
}
