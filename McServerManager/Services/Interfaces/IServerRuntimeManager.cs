using McServerManager.Models;

namespace McServerManager.Services;

public interface IServerRuntimeManager
{
    event Action<ServerConfig, int?>? ServerCrashed;

    ServerRuntime GetOrCreate(ServerConfig config);
    bool TryRelease(string serverId);
    Task StartAsync(ServerConfig config, string serverDirectory);
    Task StopAsync(ServerConfig config, int timeoutSeconds = 10);
    Task RestartAsync(ServerConfig config, string serverDirectory);
    void SendCommand(ServerConfig config, string command);
}
