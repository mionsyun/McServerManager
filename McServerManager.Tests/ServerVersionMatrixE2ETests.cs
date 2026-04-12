using McServerManager.Models;
using McServerManager.Services;
using McServerManager.Tests.TestInfrastructure;

namespace McServerManager.Tests;

public sealed class ServerVersionMatrixE2ETests
{
    [Theory]
    [InlineData("Vanilla", "1.16.5")]
    [InlineData("Vanilla", "1.17.1")]
    [InlineData("Vanilla", "1.18.2")]
    [InlineData("Vanilla", "1.19.4")]
    [InlineData("Vanilla", "1.20.1")]
    [InlineData("Vanilla", "1.20.6")]
    [InlineData("Vanilla", "1.21.1")]
    [InlineData("Paper", "1.16.5")]
    [InlineData("Paper", "1.17.1")]
    [InlineData("Paper", "1.18.2")]
    [InlineData("Paper", "1.19.4")]
    [InlineData("Paper", "1.20.1")]
    [InlineData("Paper", "1.20.6")]
    [InlineData("Paper", "1.21.1")]
    [InlineData("Purpur", "1.16.5")]
    [InlineData("Purpur", "1.17.1")]
    [InlineData("Purpur", "1.18.2")]
    [InlineData("Purpur", "1.19.4")]
    [InlineData("Purpur", "1.20.1")]
    [InlineData("Purpur", "1.20.6")]
    [InlineData("Purpur", "1.21.1")]
    [InlineData("Spigot", "1.16.5")]
    [InlineData("Spigot", "1.18.2")]
    [InlineData("Spigot", "1.20.1")]
    [InlineData("Spigot", "1.21.1")]
    [InlineData("Forge", "1.16.5")]
    [InlineData("Forge", "1.17.1")]
    [InlineData("Forge", "1.18.2")]
    [InlineData("Forge", "1.19.4")]
    [InlineData("Forge", "1.20.1")]
    [InlineData("Forge", "1.20.6")]
    [InlineData("Forge", "1.21.1")]
    [InlineData("Fabric", "1.16.5")]
    [InlineData("Fabric", "1.17.1")]
    [InlineData("Fabric", "1.18.2")]
    [InlineData("Fabric", "1.19.4")]
    [InlineData("Fabric", "1.20.1")]
    [InlineData("Fabric", "1.20.6")]
    [InlineData("Fabric", "1.21.1")]
    public async Task ProvisionStartStop_Works_ForVersionMatrix(string serverType, string version)
    {
        using var appData = new TemporaryAppDataScope();

        var paths = new AppPathsService();
        var propertiesService = new ServerPropertiesService();
        var configService = new ServerConfigService(paths);
        var fakeJarService = new FakeServerJarService();
        var provisioning = new ServerProvisioningService(paths, propertiesService, configService, fakeJarService);
        var runtimeManager = new ServerRuntimeManager();

        var baseDirectory = Directory.CreateDirectory(Path.Combine(appData.AppDataPath, "version-matrix")).FullName;
        var options = new NewServerOptions
        {
            Name = $"{serverType}-{version}",
            DirectoryPath = baseDirectory,
            Type = serverType,
            Version = version,
            MemoryXmsMb = 512,
            MemoryXmxMb = 1024,
            Port = 25565,
            MaxPlayers = 10,
            OnlineMode = false,
            EnableCommandBlock = false,
            EulaAccepted = true,
            JavaPath = "java",
            Motd = "version-matrix"
        };

        var config = await provisioning.CreateAsync(options);

        Assert.Contains(fakeJarService.Requests, request =>
            string.Equals(request.ServerType, serverType, StringComparison.OrdinalIgnoreCase)
            && string.Equals(request.VersionId, version, StringComparison.OrdinalIgnoreCase));
        Assert.True(File.Exists(Path.Combine(config.DirectoryPath, "server.jar")));
        Assert.True(File.Exists(Path.Combine(config.DirectoryPath, "config.json")));
        Assert.True(File.Exists(Path.Combine(config.DirectoryPath, "server.properties")));

        var runBatPath = Path.Combine(config.DirectoryPath, "run.bat");
        File.WriteAllText(
            runBatPath,
            "@echo off\r\necho Done (0.01s)! For help, type \"help\"\r\n:loop\r\nset /p CMD=\r\nif /I \"%CMD%\"==\"stop\" goto end\r\ngoto loop\r\n:end\r\nexit /b 0\r\n");

        config.LaunchModeOverride = "ForceForgeRunBat";
        config.AutoRestartOnCrash = false;

        await runtimeManager.StartAsync(config, config.DirectoryPath);
        var runtime = runtimeManager.GetOrCreate(config);
        await AsyncTestWait.WaitUntilAsync(() => runtime.Status == ServerStatus.Running, TimeSpan.FromSeconds(5));

        await runtimeManager.StopAsync(config, timeoutSeconds: 3);
        Assert.Equal(ServerStatus.Stopped, runtime.Status);
        Assert.False(runtime.LastStopWasForced);
        Assert.True(runtimeManager.TryRelease(config.ServerId));
    }
}
