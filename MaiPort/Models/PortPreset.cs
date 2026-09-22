namespace MaiPort.Models;

/// <summary>
/// よく使うポート設定（ワンクリックで入力欄に反映する）。
/// </summary>
public sealed record PortPreset(string Name, PortRange Range, PortProtocol Protocol, string Description);
