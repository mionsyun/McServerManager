namespace McServerManager.Models;

/// <summary>
/// サーバーの外部公開に必要なポート（範囲）。ルーター側 (External) → PC 側 (Internal) の対応を持つ。
/// </summary>
public sealed record PortRequirement(
    NetworkProtocol Protocol,
    int ExternalStart,
    int ExternalEnd,
    int InternalStart,
    string Purpose)
{
    public int Count => ExternalEnd - ExternalStart + 1;

    /// <summary>表示用（例: "TCP 19132" / "UDP 19300-19319"）。</summary>
    public string Label =>
        $"{(Protocol == NetworkProtocol.Udp ? "UDP" : "TCP")} {(Count == 1 ? ExternalStart.ToString() : $"{ExternalStart}-{ExternalEnd}")}";
}
