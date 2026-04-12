using McServerManager.Models;
using McServerManager.Services;
using McServerManager.Tests.TestInfrastructure;

namespace McServerManager.Tests;

public sealed class ServerFlowE2ETests
{
    [Fact]
    public async Task CreateStartStopRelease_Flow_Works_EndToEnd()
    {
        using var appData = new TemporaryAppDataScope();

        var paths = new AppPathsService();
        var propertiesService = new ServerPropertiesService();
        var configService = new ServerConfigService(paths);
        var fakeJarService = new FakeServerJarService();
        var provisioning = new ServerProvisioningService(paths, propertiesService, configService, fakeJarService);
        var runtimeManager = new ServerRuntimeManager();

        var baseDirectory = Directory.CreateDirectory(Path.Combine(appData.AppDataPath, "flow-e2e")).FullName;
        var options = new NewServerOptions
        {
            Name = "e2e-flow",
            DirectoryPath = baseDirectory,
            Type = "Forge",
            Version = "1.20.1",
            MemoryXmsMb = 512,
            MemoryXmxMb = 1024,
            Port = 25570,
            MaxPlayers = 12,
            OnlineMode = false,
            EnableCommandBlock = true,
            EulaAccepted = true,
            JavaPath = "java",
            Motd = "E2E test"
        };

        var progress = new List<string>();
        var config = await provisioning.CreateAsync(options, new Progress<string>(message => progress.Add(message)));

        Assert.NotEmpty(fakeJarService.Requests);
        Assert.True(Directory.Exists(config.DirectoryPath));
        Assert.True(File.Exists(Path.Combine(config.DirectoryPath, "server.jar")));
        Assert.Equal("eula=true", File.ReadAllText(Path.Combine(config.DirectoryPath, "eula.txt")).Trim());
        Assert.True(File.Exists(Path.Combine(config.DirectoryPath, "config.json")));

        var serverPropertiesText = File.ReadAllText(Path.Combine(config.DirectoryPath, "server.properties"));
        Assert.Contains("server-port=25570", serverPropertiesText, StringComparison.Ordinal);
        Assert.Contains("max-players=12", serverPropertiesText, StringComparison.Ordinal);
        Assert.Contains("online-mode=false", serverPropertiesText, StringComparison.Ordinal);

        var loaded = configService
            .LoadAll(new[] { baseDirectory })
            .SingleOrDefault(candidate => string.Equals(candidate.ServerId, config.ServerId, StringComparison.OrdinalIgnoreCase));
        Assert.NotNull(loaded);
        Assert.Equal(config.DirectoryPath, loaded!.DirectoryPath);
        Assert.Equal("Forge", loaded.Type);

        var runBatPath = Path.Combine(config.DirectoryPath, "run.bat");
        File.WriteAllText(
            runBatPath,
            "@echo off\r\necho Done (0.01s)! For help, type \"help\"\r\n:loop\r\ntimeout /t 5 /nobreak >nul\r\ngoto loop\r\n");

        config.LaunchModeOverride = "ForceForgeRunBat";
        config.AutoRestartOnCrash = false;

        await runtimeManager.StartAsync(config, config.DirectoryPath);
        var runtime = runtimeManager.GetOrCreate(config);
        await AsyncTestWait.WaitUntilAsync(() => runtime.Status == ServerStatus.Running, TimeSpan.FromSeconds(5));
        Assert.Equal(ServerStatus.Running, runtime.Status);

        await runtimeManager.StopAsync(config, timeoutSeconds: 1);
        Assert.Equal(ServerStatus.Stopped, runtime.Status);
        Assert.True(runtime.LastStopWasForced);

        Assert.True(runtimeManager.TryRelease(config.ServerId));

        Directory.Delete(config.DirectoryPath, recursive: true);
        Assert.False(Directory.Exists(config.DirectoryPath));
    }
}
