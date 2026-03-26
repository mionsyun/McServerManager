using System.Diagnostics;

namespace McServerManager.Services;

public interface INetworkService
{
    IReadOnlyList<string> GetLanIpAddresses();
    IReadOnlyList<string> GetExternalChecklist();
    Task<string?> GetPublicIpAsync();
    IReadOnlyList<Process> GetProcessesUsingPort(int port);
    bool TryKillProcessesUsingPort(int port, out string? error);
}
