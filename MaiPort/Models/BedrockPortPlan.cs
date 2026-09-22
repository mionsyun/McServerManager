namespace MaiPort.Models;

/// <summary>
/// server.properties から導いた「開放すべきポート」1件。
/// </summary>
public sealed record BedrockPortPlanItem(PortRange Range, PortProtocol Protocol, string Purpose);

/// <summary>
/// server.properties の解析結果。Notes には注意事項や推奨設定を入れる。
/// </summary>
public sealed record BedrockPortPlan(
    string Transport,
    IReadOnlyList<BedrockPortPlanItem> Items,
    IReadOnlyList<string> Notes);
