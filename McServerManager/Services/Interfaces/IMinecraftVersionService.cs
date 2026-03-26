using McServerManager.Models;

namespace McServerManager.Services;

public interface IMinecraftVersionService
{
    Task<IReadOnlyList<MinecraftVersionInfo>> GetVersionsAsync(bool forceRefresh = false);
    Task DownloadServerJarAsync(string versionId, string destinationPath);
}
