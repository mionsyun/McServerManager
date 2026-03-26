using McServerManager.Models;

namespace McServerManager.Services;

public interface IServerConfigService
{
    IReadOnlyList<ServerConfig> LoadAll(IEnumerable<string>? additionalDirectories = null);
    ServerConfig? Load(string serverId);
    void Save(ServerConfig config);
    void SaveToDirectory(ServerConfig config, string serverDirectory);
}
