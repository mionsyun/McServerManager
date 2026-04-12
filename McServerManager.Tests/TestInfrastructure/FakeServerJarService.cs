using McServerManager.Services;

namespace McServerManager.Tests.TestInfrastructure;

internal sealed class FakeServerJarService : IServerJarService
{
    public List<(string ServerType, string VersionId, string DestinationPath, string? JavaPath)> Requests { get; } = [];

    public Task DownloadAsync(string serverType, string versionId, string destinationPath, string? javaPath, IProgress<string>? progress = null)
    {
        Requests.Add((serverType, versionId, destinationPath, javaPath));
        Directory.CreateDirectory(Path.GetDirectoryName(destinationPath)!);
        File.WriteAllText(destinationPath, "fake-server-jar");
        progress?.Report($"fake-download:{serverType}:{versionId}");
        return Task.CompletedTask;
    }
}
