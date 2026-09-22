namespace MaiPort.Models;

/// <summary>
/// 開放対象のポート範囲。単一ポートの場合は Start と End が同じ値になる。
/// </summary>
public sealed record PortRange(int Start, int End);
