using MaiPort.Models;

namespace MaiPort.Services;

/// <summary>
/// Windows Defender ファイアウォールの受信規則を操作する。
/// </summary>
public interface IFirewallService
{
    bool IsAdministrator();

    string BuildRuleName(int port, PortProtocol protocol);

    Task<PortOperationResult> AllowAsync(int port, PortProtocol protocol, string description, CancellationToken ct = default);

    Task<PortOperationResult> RemoveAsync(int port, PortProtocol protocol, CancellationToken ct = default);
}
