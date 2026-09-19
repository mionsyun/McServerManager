namespace MaiPort.Models;

/// <summary>
/// 指定ポートを使用中のプロセス情報。
/// </summary>
public sealed record PortListener(PortProtocol Protocol, string LocalEndPoint, int ProcessId, string ProcessName);
