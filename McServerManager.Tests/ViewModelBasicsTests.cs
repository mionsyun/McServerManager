using McServerManager.Models;
using McServerManager.Services;
using McServerManager.Tests.TestInfrastructure;
using McServerManager.ViewModels;

namespace McServerManager.Tests;

public sealed class ViewModelBasicsTests
{
    [Fact]
    public void OptionViewModels_ExposeConstructorValues()
    {
        var launch = new LaunchModeOption("Auto", "Auto mode");
        var type = new ServerTypeOption("Paper", "Paper");
        var startup = new StartupPresetOption("Balanced", "Balanced", 1024, 2048, "-Dfile.encoding=UTF-8");
        var filter = new VersionFilterOption("release", "Release");
        var step = new TutorialStep("title", "body", "target", TutorialPlacement.Bottom, "SettingsTab");

        Assert.Equal("Auto", launch.Id);
        Assert.Equal("Paper", type.Id);
        Assert.Equal(1024, startup.MemoryXmsMb);
        Assert.Equal(2048, startup.MemoryXmxMb);
        Assert.Equal("release", filter.Id);
        Assert.Equal("target", step.TargetName);
        Assert.Equal(TutorialPlacement.Bottom, step.Placement);
    }

    [Fact]
    public void GuidePickerViewModel_CommandsInvokeExpectedGuide()
    {
        var selected = new List<GuideType>();
        var vm = new GuidePickerViewModel(selected.Add);

        vm.SelectInitialSetupCommand.Execute(null);
        vm.SelectDistributionMapCommand.Execute(null);
        vm.SelectModCommand.Execute(null);
        vm.SelectPluginCommand.Execute(null);
        vm.SelectResourcePackCommand.Execute(null);

        Assert.Equal(
            [GuideType.InitialSetup, GuideType.DistributionMap, GuideType.Mod, GuideType.Plugin, GuideType.ResourcePack],
            selected);
    }

    [Fact]
    public void ServerSettingsViewModel_LoadAndDirtyTracking_Work()
    {
        var vm = new ServerSettingsViewModel();
        var model = new ServerProperties
        {
            ServerPort = 25565,
            MaxPlayers = 20,
            Motd = "Hello",
            OnlineMode = true,
            EnableCommandBlock = false,
            Difficulty = "easy",
            GameMode = "survival",
            Pvp = true,
            ViewDistance = 10,
            SpawnProtection = 16,
            LevelName = "world",
            Seed = "123"
        };

        vm.Load(model);
        Assert.False(vm.IsDirty);

        vm.MaxPlayers = 30;
        Assert.True(vm.IsDirty);

        var roundTrip = vm.ToModel();
        Assert.Equal(30, roundTrip.MaxPlayers);
        Assert.Equal("world", roundTrip.LevelName);
    }

    [Fact]
    public async Task WorldMapViewModel_CommandsAndLoadState_Work()
    {
        var vm = new WorldMapViewModel(new StubWorldMapService(), @"C:\dummy-world");

        Assert.True(vm.ShowPlaceholder);

        vm.ZoomInCommand.Execute(null);
        Assert.True(vm.Scale > 1.0);

        vm.ZoomOutCommand.Execute(null);
        vm.ResetZoomCommand.Execute(null);
        Assert.Equal(1.0, vm.Scale);

        await vm.LoadMapAsync();
        Assert.False(vm.IsLoading);
        Assert.False(vm.HasImage);
        Assert.True(vm.ShowPlaceholder);
    }

    [Fact]
    public async Task ServerNetworkViewModel_BasicFlow_WorksWithStubs()
    {
        using var appData = new TemporaryAppDataScope();
        var paths = new AppPathsService();
        var dialog = new TestDialogService { DefaultResult = System.Windows.MessageBoxResult.Yes };
        var services = TestAppServicesFactory.CreateForAddonTests(paths, dialog);
        var config = new ServerConfig
        {
            ServerId = "net-test",
            Name = "NetworkTest",
            DirectoryPath = paths.GetServerPath("net-test"),
            Port = 25565
        };
        var settings = new AppSettings
        {
            PromptUpnp = true,
            EnableUpnp = true
        };
        var vm = new ServerNetworkViewModel(services, config, settings);

        var available = vm.EnsurePortAvailable();
        await vm.TryOpenPortAsync();
        await vm.TryClosePortAsync();

        Assert.True(available);
        Assert.NotNull(vm.LanIpAddresses);
        Assert.NotNull(vm.ExternalChecklist);
    }

    private sealed class StubWorldMapService : IWorldMapService
    {
        public Task<System.Windows.Media.Imaging.BitmapSource?> RenderWorldMapAsync(
            string worldPath,
            IProgress<string>? progress = null,
            CancellationToken cancellationToken = default)
        {
            progress?.Report("loading");
            return Task.FromResult<System.Windows.Media.Imaging.BitmapSource?>(null);
        }
    }
}
