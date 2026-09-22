using MaiPort.Models;
using MaiPort.Services;
using MaiPort.Utilities;

namespace MaiPort.ViewModels;

/// <summary>
/// 開放中ポート一覧の1行。
/// </summary>
public sealed class PortRuleViewModel : ObservableObject
{
    public PortRuleViewModel(PortRule rule, Func<PortRuleViewModel, Task> closeAsync)
    {
        Rule = rule ?? throw new ArgumentNullException(nameof(rule));
        CloseCommand = new AsyncRelayCommand(() => closeAsync(this));
    }

    public PortRule Rule { get; }

    public AsyncRelayCommand CloseCommand { get; }

    public PortRange Range => PortControlService.ToRange(Rule);

    public string Title => $"{DescribeProtocol(Rule.Protocol)} {PortRangeParser.Format(Range)}";

    public string Detail
    {
        get
        {
            var description = string.IsNullOrWhiteSpace(Rule.Description) ? "用途未設定" : Rule.Description;
            var upnp = Rule.UpnpOpened ? "UPnP 開放済み" : "UPnP なし";
            var firewall = Rule.FirewallOpened ? "ファイアウォール許可済み" : "ファイアウォール設定なし";
            return $"{description} ／ {upnp} ／ {firewall} ／ {Rule.CreatedAt:yyyy/MM/dd HH:mm}";
        }
    }

    public void RefreshDisplay()
    {
        OnPropertyChanged(nameof(Title));
        OnPropertyChanged(nameof(Detail));
    }

    internal static string DescribeProtocol(PortProtocol protocol)
    {
        return protocol switch
        {
            PortProtocol.Tcp => "TCP",
            PortProtocol.Udp => "UDP",
            _ => "TCP/UDP"
        };
    }
}
