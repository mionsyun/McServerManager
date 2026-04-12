using McServerManager.Models;
using McServerManager.Services;
using McServerManager.Tests.TestInfrastructure;
using McServerManager.ViewModels;

namespace McServerManager.Tests;

public sealed class ServerAddonViewModelE2ETests
{
    [Fact]
    public async Task ImportDisableEnableDelete_ThroughViewModelCommands_Works()
    {
        using var appData = new TemporaryAppDataScope();
        using var temp = new TemporaryDirectoryScope();
        var paths = new AppPathsService();
        var dialog = new TestDialogService { DefaultResult = System.Windows.MessageBoxResult.Yes };
        var services = TestAppServicesFactory.CreateForAddonTests(paths, dialog);

        var serverDirectory = Directory.CreateDirectory(Path.Combine(temp.Path, "paper-server")).FullName;
        var sourceDirectory = Directory.CreateDirectory(Path.Combine(temp.Path, "source")).FullName;
        var pluginJar = JarTestFactory.CreatePluginJar(sourceDirectory, "sample-plugin.jar", "SamplePlugin");

        var config = new ServerConfig
        {
            ServerId = Guid.NewGuid().ToString("N"),
            Name = "paper-e2e",
            Type = "Paper",
            Version = "1.20.1",
            DirectoryPath = serverDirectory
        };

        var viewModel = new ServerAddonViewModel(services, config);
        await viewModel.ImportAddonsAsync(new[] { pluginJar });

        Assert.Single(viewModel.Addons);
        var addon = viewModel.Addons.Single();
        Assert.True(addon.IsEnabled);
        Assert.True(File.Exists(addon.FullPath));

        viewModel.SelectedAddon = addon;
        viewModel.DisableAddonCommand.Execute(null);
        addon = Assert.Single(viewModel.Addons);
        Assert.False(addon.IsEnabled);

        viewModel.SelectedAddon = addon;
        viewModel.EnableAddonCommand.Execute(null);
        addon = Assert.Single(viewModel.Addons);
        Assert.True(addon.IsEnabled);

        viewModel.SelectedAddon = addon;
        viewModel.DeleteAddonCommand.Execute(null);
        Assert.Empty(viewModel.Addons);
    }

    [Fact]
    public async Task Import_WithCompatibilityWarnings_CanBeCancelledByDialog()
    {
        using var appData = new TemporaryAppDataScope();
        using var temp = new TemporaryDirectoryScope();
        var paths = new AppPathsService();
        var dialog = new TestDialogService();
        dialog.QueuedResults.Enqueue(System.Windows.MessageBoxResult.No);
        var services = TestAppServicesFactory.CreateForAddonTests(paths, dialog);

        var serverDirectory = Directory.CreateDirectory(Path.Combine(temp.Path, "fabric-server")).FullName;
        var sourceDirectory = Directory.CreateDirectory(Path.Combine(temp.Path, "source")).FullName;
        var forgeJar = JarTestFactory.CreateForgeModJar(sourceDirectory, "forge-only.jar", "forgeonly", new[] { "missinglib" });

        var config = new ServerConfig
        {
            ServerId = Guid.NewGuid().ToString("N"),
            Name = "fabric-e2e",
            Type = "Fabric",
            Version = "1.20.1",
            DirectoryPath = serverDirectory
        };

        var viewModel = new ServerAddonViewModel(services, config);
        await viewModel.ImportAddonsAsync(new[] { forgeJar });

        Assert.Empty(viewModel.Addons);
        Assert.NotEmpty(dialog.Calls);
        Assert.Contains(dialog.Calls, call => call.Buttons == System.Windows.MessageBoxButton.YesNo);
    }
}
