using System.Text.Json;
using McServerManager.Models;
using McServerManager.Services;
using McServerManager.Tests.TestInfrastructure;

namespace McServerManager.Tests;

public sealed class ServerConfigServiceTests
{
    [Fact]
    public void LoadAll_UsesConfigFileDirectory_InsteadOfSerializedDirectoryPath()
    {
        using var appDataScope = new TemporaryAppDataScope();
        var paths = new AppPathsService();
        var service = new ServerConfigService(paths);

        var externalRoot = Directory.CreateDirectory(System.IO.Path.Combine(appDataScope.AppDataPath, "external")).FullName;
        var serverDirectory = Directory.CreateDirectory(System.IO.Path.Combine(externalRoot, "srv-loadall")).FullName;
        var configPath = System.IO.Path.Combine(serverDirectory, "config.json");

        var config = new ServerConfig
        {
            ServerId = "srv-loadall",
            Name = "loadall-test",
            DirectoryPath = @"C:\tampered\path"
        };
        File.WriteAllText(configPath, JsonSerializer.Serialize(config));

        var loaded = service.LoadAll(new[] { externalRoot }).Single(c => c.ServerId == "srv-loadall");

        Assert.Equal(serverDirectory, loaded.DirectoryPath);
    }

    [Fact]
    public void Load_UsesConfigFileDirectory_InsteadOfSerializedDirectoryPath()
    {
        using var appDataScope = new TemporaryAppDataScope();
        var paths = new AppPathsService();
        var service = new ServerConfigService(paths);

        const string serverId = "srv-load";
        var serverDirectory = Directory.CreateDirectory(paths.GetServerPath(serverId)).FullName;
        var configPath = paths.GetServerConfigPath(serverId);
        var config = new ServerConfig
        {
            ServerId = serverId,
            Name = "load-test",
            DirectoryPath = @"D:\tampered\path"
        };
        File.WriteAllText(configPath, JsonSerializer.Serialize(config));

        var loaded = service.Load(serverId);

        Assert.NotNull(loaded);
        Assert.Equal(serverDirectory, loaded!.DirectoryPath);
    }
}
