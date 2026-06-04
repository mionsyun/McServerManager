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

    private string _publicIp = "-";
    private string _publicIpStatus = string.Empty;
    private bool _upnpOpened;

    public ServerNetworkViewModel(AppServices services, ServerConfig config, AppSettings appSettings)
    {
        _services = services;
        _config = config;
        _appSettings = appSettings;

        LanIpAddresses = new ObservableCollection<string>(_services.Network.GetLanIpAddresses());
        ExternalChecklist = new ObservableCollection<string>(_services.Network.GetExternalChecklist());

        CreateFirewallRuleCommand = new RelayCommand(_ => CreateFirewallRule());
        DeleteFirewallRuleCommand = new RelayCommand(_ => DeleteFirewallRule());
        RecreateFirewallRuleCommand = new RelayCommand(_ => RecreateFirewallRule());
        OpenPortCommand = new AsyncRelayCommand(OpenPortAsync);
        ClosePortCommand = new AsyncRelayCommand(ClosePortAsync);
        RefreshPublicIpCommand = new AsyncRelayCommand(RefreshPublicIpAsync);
        CopyShareAddressCommand = new RelayCommand(_ => CopyShareAddress());
    }

    public ObservableCollection<string> LanIpAddresses { get; }
    public ObservableCollection<string> ExternalChecklist { get; }

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

    /// <summary>サーバー起動時にServerViewModelから呼び出す。</summary>
    public async Task TryOpenPortAsync()
    {
        if (_appSettings.PromptUpnp)
        {
            var result = _services.Dialog.Show(
                "ルーターの自動ポート開放を試しますか？",
                "確認", MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (result != MessageBoxResult.Yes)
                return;
        }
        else if (!_appSettings.EnableUpnp)
        {
            return;
        }

        var (ok, error) = await _services.Upnp.TryOpenPortAsync(
            Port, $"McServerManager_{_config.Name}");
        if (!ok)
        {
            _services.Dialog.Show(
                error ?? "ポート開放に失敗しました。",
                "警告", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        _upnpOpened = true;
    }

    /// <summary>サーバー停止時にServerViewModelから呼び出す。</summary>
    public async Task TryClosePortAsync()
    {
        if (!_upnpOpened)
            return;
        await _services.Upnp.TryClosePortAsync(Port);
        _upnpOpened = false;
    }

    /// <summary>サーバー起動前にServerViewModelから呼び出す。</summary>
    public bool EnsurePortAvailable()
    {
        var processes = _services.Network.GetProcessesUsingPort(Port);
        if (processes.Count == 0)
            return true;

        var names = string.Join(", ", processes.Select(p => $"{p.ProcessName}({p.Id})"));
        var result = _services.Dialog.Show(
            $"ポート {Port} を使用しているプロセスがあります: {names}\n終了させて続行しますか？",
            "ポート使用中", MessageBoxButton.YesNo, MessageBoxImage.Warning);

        if (result != MessageBoxResult.Yes)
            return false;

        if (!_services.Network.TryKillProcessesUsingPort(Port, out var error))
        {
            _services.Dialog.Show(
                $"プロセス終了に失敗しました: {error}",
                "エラー", MessageBoxButton.OK, MessageBoxImage.Error);
            return false;
        }

        return true;
    }

    private void CreateFirewallRule()
    {
        try
        {
            if (string.IsNullOrWhiteSpace(_config.Firewall.TcpRuleName))
                _config.Firewall = _services.Firewall.BuildRuleInfo(_config.ServerId);

            _services.Firewall.CreateRules(Port, _config.Firewall);
            _services.Configs.Save(_config);
            OnPropertyChanged(nameof(IsFirewallConfigured));
            _services.Dialog.Show(
                "Firewall ルールを作成しました。",
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

    private void RecreateFirewallRule()
    {
        try
        {
            if (string.IsNullOrWhiteSpace(_config.Firewall.TcpRuleName))
                _config.Firewall = _services.Firewall.BuildRuleInfo(_config.ServerId);

            _services.Firewall.RecreateRules(Port, _config.Firewall);
            _services.Configs.Save(_config);
            OnPropertyChanged(nameof(IsFirewallConfigured));
            _services.Dialog.Show(
                "Firewall ルールを再作成しました。",
                "完了", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            _services.Dialog.Show(
                $"Firewall ルール再作成に失敗しました: {ex.Message}",
                "エラー", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private async Task OpenPortAsync()
    {
        var (ok, error) = await _services.Upnp.TryOpenPortAsync(
            Port, $"McServerManager_{_config.Name}");
        if (!ok)
        {
            _services.Dialog.Show(
                error ?? "ポート開放に失敗しました。",
                "警告", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        _upnpOpened = true;
        OnPropertyChanged(nameof(IsUpnpOpen));
        _services.Dialog.Show(
            $"TCP {Port} のポート開放を実行しました。",
            "完了", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private async Task ClosePortAsync()
    {
        await _services.Upnp.TryClosePortAsync(Port);
        _upnpOpened = false;
        OnPropertyChanged(nameof(IsUpnpOpen));
        _services.Dialog.Show(
            $"TCP {Port} のポート閉鎖を実行しました。",
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
