using System.Diagnostics;
using McServerManager.Models;

namespace McServerManager.Services;

public interface INetworkService
{
    IReadOnlyList<string> GetLanIpAddresses();
    IReadOnlyList<string> GetExternalChecklist(NetworkProtocol protocol = NetworkProtocol.Tcp);
    Task<string?> GetPublicIpAsync();
    IReadOnlyList<Process> GetProcessesUsingPort(int port, NetworkProtocol protocol = NetworkProtocol.Tcp);
    bool TryKillProcessesUsingPort(int port, out string? error, NetworkProtocol protocol = NetworkProtocol.Tcp);
}
