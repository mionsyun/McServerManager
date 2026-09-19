using MaiPort.Services;
using MaiPort.Utilities;

namespace MaiPort.ViewModels;

/// <summary>
/// この PC のネットワーク状態（管理者権限・LAN IP・グローバル IP・UPnP）を表示する。
/// </summary>
public sealed class NetworkStatusViewModel : ObservableObject
{
    private readonly INetworkService _network;
    private readonly IUpnpService _upnp;
    private readonly IFirewallService _firewall;

    private string _lanIpText = "取得中…";
    private string _publicIpText = "取得中…";
    private string _upnpText = "確認中…";
    private bool _isAdministrator;
    private bool _isBusy;

    public NetworkStatusViewModel(INetworkService network, IUpnpService upnp, IFirewallService firewall)
    {
        _network = network;
        _upnp = upnp;
        _firewall = firewall;
        RefreshCommand = new AsyncRelayCommand(RefreshAsync, () => !IsBusy);
    }

    public AsyncRelayCommand RefreshCommand { get; }

    public string LanIpText
    {
        get => _lanIpText;
        private set => SetProperty(ref _lanIpText, value);
    }

    public string PublicIpText
    {
        get => _publicIpText;
        private set => SetProperty(ref _publicIpText, value);
    }

    public string UpnpText
    {
        get => _upnpText;
        private set => SetProperty(ref _upnpText, value);
    }

    public bool IsAdministrator
    {
        get => _isAdministrator;
        private set
        {
            if (SetProperty(ref _isAdministrator, value))
            {
                OnPropertyChanged(nameof(AdministratorText));
            }
        }
    }

    public string AdministratorText => IsAdministrator
        ? "管理者として実行中（ファイアウォール設定がそのまま反映されます）"
        : "通常ユーザーで実行中（ファイアウォール設定時にUACの確認が出ます）";

    public bool IsBusy
    {
        get => _isBusy;
        private set
        {
            if (SetProperty(ref _isBusy, value))
            {
                RefreshCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public async Task RefreshAsync()
    {
        IsBusy = true;
        try
        {
            IsAdministrator = _firewall.IsAdministrator();

            var lanAddresses = _network.GetLanIpAddresses();
            LanIpText = lanAddresses.Count == 0 ? "取得できませんでした" : string.Join(" / ", lanAddresses);

            UpnpText = "確認中…";
            PublicIpText = "取得中…";

            var upnpAvailable = await _upnp.IsDeviceAvailableAsync();
            UpnpText = upnpAvailable
                ? "UPnP対応ルーターを検出しました"
                : "UPnP対応ルーターが見つかりません（ルーターの設定を確認してください）";

            var publicIp = await _network.GetPublicIpAsync();
            if (publicIp is null && upnpAvailable)
            {
                publicIp = await _upnp.GetExternalIpAsync();
            }

            PublicIpText = publicIp ?? "取得できませんでした";
        }
        finally
        {
            IsBusy = false;
        }
    }
}
