using MaiPort.Models;

namespace MaiPort.Services;

/// <summary>
/// ローカル / グローバル IP とポート使用状況を取得する。
/// </summary>
public interface INetworkService
{
    IReadOnlyList<string> GetLanIpAddresses();

    Task<string?> GetPublicIpAsync(CancellationToken ct = default);

    IReadOnlyList<PortListener> GetListeners(int port, PortProtocol protocol);
}
