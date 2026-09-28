using McServerManager.Models;

namespace McServerManager.Services;

/// <summary>統合版 (BDS) の server.properties と allowlist.json の読み書き。</summary>
public interface IBedrockPropertiesService
{
    BedrockServerProperties Load(string serverDirectory);
    void Save(string serverDirectory, BedrockServerProperties properties);
    IReadOnlyList<BedrockAllowlistEntry> LoadAllowlist(string serverDirectory);
    void SaveAllowlist(string serverDirectory, IEnumerable<BedrockAllowlistEntry> entries);
}
