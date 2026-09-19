namespace MaiPort.Models;

/// <summary>
/// ポート開放 / 解除の実行結果。Messages は画面ログへそのまま流す。
/// </summary>
public sealed record PortControlOutcome(bool Success, IReadOnlyList<string> Messages, PortRule? Rule);
