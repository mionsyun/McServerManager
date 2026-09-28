using McServerManager.Models;

namespace McServerManager.Services;

public interface IUpnpService
{
    /// <param name="port">ルーター側 (外部) のポート。</param>
    /// <param name="privatePort">PC 側のポート。省略時は <paramref name="port"/> と同じ。</param>
    Task<(bool ok, string? error)> TryOpenPortAsync(int port, string description, NetworkProtocol protocol = NetworkProtocol.Tcp, int? privatePort = null);
    Task TryClosePortAsync(int port, NetworkProtocol protocol = NetworkProtocol.Tcp);
}
