using MaiPort.Models;

namespace MaiPort.Services;

/// <summary>
/// ルーターの UPnP (IGD) によるポートマッピングを操作する。
/// </summary>
public interface IUpnpService
{
    Task<PortOperationResult> OpenAsync(PortRange range, PortProtocol protocol, string description, CancellationToken ct = default);

    Task<PortOperationResult> CloseAsync(PortRange range, PortProtocol protocol, CancellationToken ct = default);

    Task<bool> IsDeviceAvailableAsync(CancellationToken ct = default);

    Task<string?> GetExternalIpAsync(CancellationToken ct = default);
}
