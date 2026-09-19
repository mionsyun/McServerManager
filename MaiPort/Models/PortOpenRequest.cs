namespace MaiPort.Models;

/// <summary>
/// ポート開放の要求内容。
/// </summary>
public sealed record PortOpenRequest(int Port, PortProtocol Protocol, string Description, bool UseUpnp, bool UseFirewall);
