using MaiPort.Models;

namespace MaiPort.Services;

/// <summary>
/// Minecraft 統合版サーバー（Bedrock Dedicated Server）の server.properties を読み、
/// 開放すべきポートを判定する。
/// </summary>
public interface IBedrockServerService
{
    Task<BedrockPortPlan> LoadPlanAsync(string serverPropertiesPath, CancellationToken ct = default);

    BedrockPortPlan BuildPlan(IReadOnlyDictionary<string, string> properties);
}
