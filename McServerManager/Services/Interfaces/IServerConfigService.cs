using McServerManager.Models;

namespace McServerManager.Services;

public interface IServerConfigService
{
    IReadOnlyList<ServerConfig> LoadAll(IEnumerable<string>? additionalDirectories = null);
    ServerConfig? Load(string serverId);
    /// <summary>指定したサーバーフォルダ直下の config.json だけを読む（他のサーバーは探さない）。</summary>
    ServerConfig? LoadFromDirectory(string serverDirectory);
    void Save(ServerConfig config);
    void SaveToDirectory(ServerConfig config, string serverDirectory);
}
