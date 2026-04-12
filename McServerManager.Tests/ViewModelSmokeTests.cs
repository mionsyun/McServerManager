using System.Windows;
using McServerManager.Models;
using McServerManager.Services;
using McServerManager.Tests.TestInfrastructure;
using McServerManager.ViewModels;

namespace McServerManager.Tests;

public sealed class ViewModelSmokeTests
{
    [Fact]
    public void MainViewModel_CanConstruct()
    {
        using var appData = new TemporaryAppDataScope();
        var paths = new AppPathsService();
        var dialog = new TestDialogService();
        var services = TestAppServicesFactory.CreateForAddonTests(paths, dialog);

        var vm = new MainViewModel(services);

        Assert.NotNull(vm.Servers);
        Assert.NotNull(vm.Tutorial);
    }

    [Fact]
    public void ServerWorldViewModel_CanConstructAndCreateWorld()
    {
        using var appData = new TemporaryAppDataScope();
        var paths = new AppPathsService();
        var dialog = new TestDialogService();
        var services = TestAppServicesFactory.CreateForAddonTests(paths, dialog);
        var serverDirectory = Directory.CreateDirectory(Path.Combine(appData.AppDataPath, "world-smoke")).FullName;
        Directory.CreateDirectory(Path.Combine(serverDirectory, "world"));
        Directory.CreateDirectory(Path.Combine(serverDirectory, "world", "region"));

        var config = new ServerConfig
        {
            ServerId = "world-smoke",
            Name = "world-smoke",
            DirectoryPath = serverDirectory,
            WorldName = "world"
        };
        var settings = new ServerSettingsViewModel();
        settings.Load(new ServerProperties { LevelName = "world" });

        var vm = new ServerWorldViewModel(services, config, settings, () => ServerStatus.Stopped);
        vm.NewWorldName = "world2";
        vm.CreateWorldCommand.Execute(null);

        Assert.Contains("world", vm.Worlds);
        Assert.DoesNotContain("world2", vm.Worlds);
        Assert.True(Directory.Exists(Path.Combine(serverDirectory, "world2")));
    }

    [Fact]
    public void ServerResourcePackViewModel_CanConstructAndDispose()
    {
        using var appData = new TemporaryAppDataScope();
        var paths = new AppPathsService();
        var dialog = new TestDialogService();
        var services = TestAppServicesFactory.CreateForAddonTests(paths, dialog);
        var config = new ServerConfig
        {
            ServerId = "rp-smoke",
            Name = "rp-smoke",
            DirectoryPath = paths.GetServerPath("rp-smoke")
        };

        using var vm = new ServerResourcePackViewModel(services, config);

        Assert.False(vm.IsServerRunning);
    }

    [Fact]
    public void ServerViewModel_CanConstructAndDispose_OnStaThread()
    {
        StaThreadRunner.Run(() =>
        {
            using var appData = new TemporaryAppDataScope();
            if (Application.Current is null)
            {
                _ = new Application();
            }

            var paths = new AppPathsService();
            var dialog = new TestDialogService();
            var services = TestAppServicesFactory.CreateForAddonTests(paths, dialog);
            var serverDirectory = Directory.CreateDirectory(Path.Combine(appData.AppDataPath, "server-vm-smoke")).FullName;
            Directory.CreateDirectory(Path.Combine(serverDirectory, "world"));

            var config = new ServerConfig
            {
                ServerId = "server-vm-smoke",
                Name = "server-vm-smoke",
                DirectoryPath = serverDirectory,
                WorldName = "world",
                Type = "Vanilla",
                Version = "1.20.1"
            };

            using var vm = new ServerViewModel(services, config);
            Assert.Equal("server-vm-smoke", vm.Name);
        });
    }
}
