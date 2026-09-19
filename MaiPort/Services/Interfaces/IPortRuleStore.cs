using MaiPort.Models;

namespace MaiPort.Services;

/// <summary>
/// 開放済みポートの記録を永続化する。
/// </summary>
public interface IPortRuleStore
{
    Task<IReadOnlyList<PortRule>> LoadAsync(CancellationToken ct = default);

    Task SaveAsync(IReadOnlyList<PortRule> rules, CancellationToken ct = default);
}
