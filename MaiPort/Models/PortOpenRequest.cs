namespace MaiPort.Models;

/// <summary>
/// ポート開放の要求内容。
/// </summary>
public sealed record PortOpenRequest(PortRange Range, PortProtocol Protocol, string Description, bool UseUpnp, bool UseFirewall);
