namespace McServerManager.Models;

/// <summary>サーバーの外部公開に必要なポート一覧と、設定上の注意点。</summary>
public sealed record PortPlan(
    IReadOnlyList<PortRequirement> Ports,
    bool UsesNetherNet,
    IReadOnlyList<string> Warnings);
