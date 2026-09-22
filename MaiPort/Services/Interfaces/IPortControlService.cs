using MaiPort.Models;

namespace MaiPort.Services;

/// <summary>
/// ファイアウォール・UPnP・記録の更新をまとめて行う。
/// </summary>
public interface IPortControlService
{
    Task<IReadOnlyList<PortRule>> GetRulesAsync(CancellationToken ct = default);

    Task<PortControlOutcome> OpenAsync(PortOpenRequest request, CancellationToken ct = default);

    Task<PortControlOutcome> CloseAsync(PortRange range, PortProtocol protocol, CancellationToken ct = default);
}
