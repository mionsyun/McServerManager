using System.Windows;
using MaiPort.Services;
using MaiPort.ViewModels;

namespace MaiPort;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // 依存はここで組み立て、各クラスには必要なものだけをコンストラクタで渡す。
        var network = new NetworkService();
        var upnp = new UpnpService();
        var firewall = new FirewallService();
        var store = new PortRuleStore();
        var dialog = new DialogService();
        var clipboard = new ClipboardService();
        var fileDialog = new FileDialogService();
        var bedrockServer = new BedrockServerService();
        var control = new PortControlService(upnp, firewall, store);

        var log = new OperationLogViewModel();
        var forward = new PortForwardViewModel(control, network, dialog, log);
        var status = new NetworkStatusViewModel(network, upnp, firewall);
        var bedrock = new BedrockSetupViewModel(bedrockServer, control, fileDialog, dialog, log, () => forward.RefreshRulesAsync());
        var mainViewModel = new MainViewModel(forward, status, log, bedrock, clipboard);

        var window = new MainWindow(mainViewModel);
        MainWindow = window;
        window.Show();
    }
}
