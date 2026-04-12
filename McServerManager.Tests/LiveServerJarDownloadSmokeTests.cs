using McServerManager.Services;
using McServerManager.Tests.TestInfrastructure;

namespace McServerManager.Tests;

public sealed class LiveServerJarDownloadSmokeTests
{
    [SkippableTheory]
    [InlineData("Vanilla", "1.16.5")]
    [InlineData("Vanilla", "1.20.1")]
    [InlineData("Vanilla", "1.21.1")]
    [InlineData("Paper", "1.16.5")]
    [InlineData("Paper", "1.20.1")]
    [InlineData("Paper", "1.21.1")]
    [InlineData("Purpur", "1.16.5")]
    [InlineData("Purpur", "1.20.1")]
    [InlineData("Purpur", "1.21.1")]
    [InlineData("Fabric", "1.16.5")]
    [InlineData("Fabric", "1.20.1")]
    [InlineData("Fabric", "1.21.1")]
    [InlineData("Spigot", "1.16.5")]
    [InlineData("Spigot", "1.20.1")]
    [InlineData("Spigot", "1.21.1")]
    public async Task DownloadAsync_LiveSmoke_ProducesServerJar(string serverType, string version)
    {
        Skip.IfNot(
            LiveTestGate.IsLiveEnabled() && LiveTestGate.IsLiveDownloadEnabled(),
            $"Set {LiveTestGate.LiveEnvVar}=1 and {LiveTestGate.LiveDownloadEnvVar}=1 to run live download tests.");

        if (string.Equals(serverType, "Spigot", StringComparison.OrdinalIgnoreCase))
        {
            Skip.IfNot(
                LiveTestGate.CanResolveAnyHost("download.getbukkit.org", "cdn.getbukkit.org"),
                "Neither download.getbukkit.org nor cdn.getbukkit.org is resolvable in this environment.");
        }

        using var appData = new TemporaryAppDataScope();
        using var temp = new TemporaryDirectoryScope();
        var paths = new AppPathsService();
        var versionService = new MinecraftVersionService(paths);
        var javaService = new JavaService();
        var jarService = new ServerJarService(versionService, javaService);

        var serverDirectory = Directory.CreateDirectory(Path.Combine(temp.Path, $"{serverType}-{version}")).FullName;
        var destinationPath = Path.Combine(serverDirectory, "server.jar");

        await jarService.DownloadAsync(serverType, version, destinationPath, javaPath: null);

        Assert.True(File.Exists(destinationPath), $"server.jar was not created for {serverType} {version}");
        var length = new FileInfo(destinationPath).Length;
        Assert.True(length > 0, $"server.jar is empty for {serverType} {version}");
    }

    [SkippableTheory]
    [InlineData("1.20.1")]
    [InlineData("1.21.1")]
    public async Task DownloadAsync_Forge_LiveSmoke_ProducesServerJar(string version)
    {
        Skip.IfNot(
            LiveTestGate.IsLiveEnabled()
            && LiveTestGate.IsLiveDownloadEnabled()
            && LiveTestGate.IsLiveForgeDownloadEnabled(),
            $"Set {LiveTestGate.LiveEnvVar}=1, {LiveTestGate.LiveDownloadEnvVar}=1 and {LiveTestGate.LiveForgeDownloadEnvVar}=1 to run Forge live download test.");

        using var appData = new TemporaryAppDataScope();
        using var temp = new TemporaryDirectoryScope();
        var paths = new AppPathsService();
        var versionService = new MinecraftVersionService(paths);
        var javaService = new JavaService();
        var jarService = new ServerJarService(versionService, javaService);

        var serverDirectory = Directory.CreateDirectory(Path.Combine(temp.Path, $"Forge-{version}")).FullName;
        var destinationPath = Path.Combine(serverDirectory, "server.jar");

        await jarService.DownloadAsync("Forge", version, destinationPath, javaPath: null);

        Assert.True(File.Exists(destinationPath), $"server.jar was not created for Forge {version}");
        var length = new FileInfo(destinationPath).Length;
        Assert.True(length > 0, $"Forge server.jar is empty for Forge {version}");
    }
}
