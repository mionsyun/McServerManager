using System.Reflection;
using System.Windows;
using McServerManager.Models;
using McServerManager.Services;
using McServerManager.Services.Editions;
using McServerManager.Services.Templates;
using McServerManager.Tests.TestInfrastructure;
using McServerManager.ViewModels;

namespace McServerManager.Tests;

public sealed class RuntimeUnknownViewModelTests
{
    [Fact]
    public void UnknownRuntime_DoesNotAppearStopped_BlocksStartAndRestart_AndAllowsStop()
    {
        StaThreadRunner.Run(() =>
        {
            EnsureApplication();
            using var appData = new TemporaryAppDataScope();
            var services = TestAppServicesFactory.CreateForAddonTests(new AppPathsService(), new TestDialogService());
            var config = CreateConfig(services.Paths);
            var runtime = services.Runtime.GetOrCreate(config);

            // Set only in-memory state. No executable is created or process started.
            InjectStatusForTest(runtime, ServerStatus.Unknown);

            using var vm = CreateServerViewModel(services, config);
            vm.World.SelectedWorld = "world";
            vm.World.RestoreBackupWorldName = "world";
            vm.World.SelectedWorldBackup = new WorldBackupEntry
            {
                FileName = "test-backup.zip",
                FullPath = Path.Combine(config.DirectoryPath, "backups", "test-backup.zip"),
                WorldName = "world"
            };

            Assert.Null(runtime.Process);
            Assert.Equal(ServerStatus.Unknown, vm.Status);
            Assert.Equal("停止を確認できません", vm.StatusText);
            Assert.Equal("■ 停止を再確認", vm.StopButtonLabel);
            Assert.False(vm.IsStopped);
            Assert.False(vm.IsRunning);
            Assert.False(vm.StartCommand.CanExecute(null));
            Assert.False(vm.RestartCommand.CanExecute(null));
            Assert.True(vm.StopCommand.CanExecute(null));
            Assert.False(vm.World.CreateWorldBackupCommand.CanExecute(null));
            Assert.False(vm.World.RestoreWorldBackupCommand.CanExecute(null));
        });
    }

    [Fact]
    public void DeleteServer_WhenReleaseFailsAfterConfirmation_PreservesServerAndFiles()
    {
        StaThreadRunner.Run(() =>
        {
            EnsureApplication();
            using var appData = new TemporaryAppDataScope();
            var dialog = new StateChangingDialog();
            var services = TestAppServicesFactory.CreateForAddonTests(new AppPathsService(), dialog);
            var config = CreateConfig(services.Paths);
            services.Configs.Save(config);
            var sentinelPath = Path.Combine(config.DirectoryPath, "keep-world-data.txt");
            File.WriteAllText(sentinelPath, "isolated test world data");
            var configPath = Path.Combine(config.DirectoryPath, "config.json");
            var configBefore = File.ReadAllBytes(configPath);
            var bedrockProperties = new BedrockPropertiesService();
            var main = new MainViewModel(
                services,
                new StubBackupSchedulerService(),
                new StubBedrockServerService(),
                bedrockProperties,
                new PortForwardingService(new StubUpnpService(), bedrockProperties),
                new StubJavaRuntimeInstaller(),
                new TemplateManifestService(),
                new StubModInspectionService(),
                new EditionViewModel(EditionPolicy.Current, ProDistributionLinks.Current,
                    new ProStoreNavigation(ProDistributionLinks.Current)));
            var target = Assert.Single(main.Servers);

            try
            {
                var runtime = services.Runtime.GetOrCreate(config);
                Assert.Equal(ServerStatus.Stopped, target.Status);
                Assert.True(target.IsStopped);
                Assert.True(target.StartCommand.CanExecute(null));
                Assert.False(target.StopCommand.CanExecute(null));
                dialog.Recorded.Calls.Clear();

                // A modal confirmation can admit runtime changes after the initial
                // Stopped check. Simulate that race without any operating-system process.
                dialog.OnConfirmation = () => InjectStatusForTest(runtime, ServerStatus.Unknown);

                main.DeleteServerCommand.Execute(target);

                Assert.Equal(ServerStatus.Unknown, runtime.Status);
                Assert.Null(runtime.Process);
                Assert.Same(target, Assert.Single(main.Servers));
                Assert.Same(target, main.SelectedServer);
                Assert.Equal("isolated test world data", File.ReadAllText(sentinelPath));
                Assert.Equal(configBefore, File.ReadAllBytes(configPath));
                Assert.Same(runtime, services.Runtime.GetOrCreate(config));
                Assert.Equal(2, dialog.Recorded.Calls.Count);
                Assert.Equal(MessageBoxButton.YesNo, dialog.Recorded.Calls[0].Buttons);
                Assert.Contains("削除を中止しました", dialog.Recorded.Calls[1].Message);
            }
            finally
            {
                target.Dispose();
            }
        });
    }

    private static void EnsureApplication()
    {
        if (Application.Current is null)
            _ = new Application();
    }

    private static void InjectStatusForTest(ServerRuntime runtime, ServerStatus status)
    {
        // Production callers cannot mutate runtime status. Deliberately inject only
        // presentation state here, without granting any process-ownership evidence.
        var setter = typeof(ServerRuntime).GetMethod("SetStatus",
            BindingFlags.Instance | BindingFlags.NonPublic,
            binder: null, types: [typeof(ServerStatus)], modifiers: null);
        Assert.NotNull(setter);
        setter!.Invoke(runtime, [status]);
    }

    private static ServerConfig CreateConfig(AppPathsService paths)
    {
        var id = Guid.NewGuid().ToString("N");
        var directory = Directory.CreateDirectory(paths.GetServerPath(id)).FullName;
        Directory.CreateDirectory(Path.Combine(directory, "world", "region"));
        return new ServerConfig
        {
            ServerId = id,
            Name = "runtime-unknown-test",
            DirectoryPath = directory,
            WorldName = "world",
            // Bedrock skips the Java/version-loading constructor path. All services
            // that could contact the network are supplied by the existing test stubs.
            Type = ServerEditions.BedrockType,
            Version = "1.26.60.3",
            AutoRestartOnCrash = false
        };
    }

    private static ServerViewModel CreateServerViewModel(AppServices services, ServerConfig config)
    {
        var bedrockProperties = new BedrockPropertiesService();
        return new ServerViewModel(
            services,
            new StubBackupSchedulerService(),
            new StubBedrockServerService(),
            bedrockProperties,
            new PortForwardingService(new StubUpnpService(), bedrockProperties),
            new StubJavaRuntimeInstaller(),
            config);
    }

    private sealed class StateChangingDialog : IDialogService
    {
        internal TestDialogService Recorded { get; } = new();
        internal Action? OnConfirmation { get; set; }

        public MessageBoxResult Show(string message, string title,
            MessageBoxButton buttons = MessageBoxButton.OK,
            MessageBoxImage image = MessageBoxImage.None)
        {
            var result = Recorded.Show(message, title, buttons, image);
            if (buttons == MessageBoxButton.YesNo)
                OnConfirmation?.Invoke();
            return result;
        }
    }
}
