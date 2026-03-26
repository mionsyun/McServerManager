using McServerManager.Models;

namespace McServerManager.Services;

public interface IServerPropertiesService
{
    ServerProperties Load(string serverDirectory, ServerConfig fallback);
    void Save(string serverDirectory, ServerProperties properties);
}
