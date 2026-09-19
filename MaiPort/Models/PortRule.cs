namespace MaiPort.Models;

/// <summary>
/// 開放済みポートの記録（JSON永続化の単位）。
/// </summary>
public sealed class PortRule
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public int Port { get; set; }
    public PortProtocol Protocol { get; set; } = PortProtocol.Udp;
    public string Description { get; set; } = string.Empty;
    public bool UpnpOpened { get; set; }
    public bool FirewallOpened { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.Now;
}
