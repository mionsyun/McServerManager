namespace McServerManager.Services;

public interface IServerJarService
{
    Task DownloadAsync(string serverType, string versionId, string destinationPath, string? javaPath, IProgress<string>? progress = null);
}
