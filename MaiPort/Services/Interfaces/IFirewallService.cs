using MaiPort.Models;

namespace MaiPort.Services;

/// <summary>
/// Windows Defender ファイアウォールの受信規則を操作する。
/// </summary>
public interface IFirewallService
{
    bool IsAdministrator();

    string BuildRuleName(PortRange range, PortProtocol protocol);

    Task<PortOperationResult> AllowAsync(PortRange range, PortProtocol protocol, string description, CancellationToken ct = default);

    Task<PortOperationResult> RemoveAsync(PortRange range, PortProtocol protocol, CancellationToken ct = default);
}
