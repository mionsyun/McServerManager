using System.IO;
using System.Windows;
using McServerManager.Models;
using McServerManager.Services;
using McServerManager.ViewModels;
using McServerManager.Views;

namespace McServerManager;

public partial class App : System.Windows.Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        RegisterExceptionLogging();

        try
        {
            var paths = new AppPathsService();
            var settingsService = new AppSettingsService(paths);
            var configService = new ServerConfigService(paths);
            var propertiesService = new ServerPropertiesService();
            var versionService = new MinecraftVersionService(paths);
            var runtimeManager = new ServerRuntimeManager();
            var worldService = new WorldService();
            var worldMapService = new WorldMapService();
            var permissionsService = new PermissionsService();
            var firewallService = new FirewallService();
            var networkService = new NetworkService();
            var upnpService = new UpnpService();
            var dialogService = new DialogService();
            var themeService = new ThemeService();
            var javaService = new JavaService();
            var addonManagementService = new AddonManagementService();
            var addonCatalogService = new AddonCatalogService();
            var appUpdateService = new AppUpdateService();
            var jarService = new ServerJarService(versionService, javaService);
            var provisioningService = new ServerProvisioningService(paths, propertiesService, configService, jarService);

            var appSettings = settingsService.Load();
            themeService.Apply(appSettings.Theme);

            var services = new AppServices(
                paths,
                settingsService,
                configService,
                runtimeManager,
                propertiesService,
                versionService,
                provisioningService,
                jarService,
                worldService,
                worldMapService,
                permissionsService,
                firewallService,
                networkService,
                upnpService,
                dialogService,
                themeService,
                javaService,
                addonManagementService,
                addonCatalogService,
                appUpdateService);

            var mainWindow = new MainWindow
            {
                DataContext = new MainViewModel(services)
            };

            MainWindow = mainWindow;
            mainWindow.Show();

            ShowFirstRunGuideIfNeeded(settingsService, mainWindow);
            if (mainWindow.DataContext is MainViewModel vm)
            {
                _ = vm.CheckForAppUpdateOnStartupAsync();
            }
        }
        catch (Exception ex)
        {
            LogException("OnStartup", ex);
            System.Windows.MessageBox.Show(
                $"Startup failed. Log: {GetLogPath()}",
                "MaiPilot",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            Shutdown(-1);
        }
    }

    private void ShowFirstRunGuideIfNeeded(AppSettingsService settingsService, MainWindow mainWindow)
    {
        var settings = settingsService.Load();
        if (settings.HasShownFirstRun)
        {
            StartTutorialIfNeeded(settings, mainWindow);
            return;
        }

        var guide = new FirstRunWindow
        {
            Owner = MainWindow
        };
        guide.ShowDialog();

        settings.HasShownFirstRun = true;
        settingsService.Save(settings);
        StartTutorialIfNeeded(settings, mainWindow);
    }

    private static void StartTutorialIfNeeded(AppSettings settings, MainWindow mainWindow)
    {
        if (settings.HasCompletedTutorial)
        {
            return;
        }

        if (mainWindow.DataContext is MainViewModel vm)
        {
            vm.StartTutorial();
        }
    }

    private static void RegisterExceptionLogging()
    {
        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
        {
            LogUnhandled("AppDomain", args.ExceptionObject);
        };

        if (Current is not null)
        {
            Current.DispatcherUnhandledException += (_, args) =>
            {
                LogException("Dispatcher", args.Exception);
                System.Windows.MessageBox.Show(
                    $"Unexpected error. Log: {GetLogPath()}",
                    "MaiPilot",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
                args.Handled = true;
                Current.Shutdown(-1);
            };
        }

        TaskScheduler.UnobservedTaskException += (_, args) =>
        {
            LogException("TaskScheduler", args.Exception);
            args.SetObserved();
        };
    }

    private static void LogUnhandled(string context, object exceptionObject)
    {
        if (exceptionObject is Exception ex)
        {
            LogException(context, ex);
            return;
        }

        WriteLog($"{context}: Non-exception unhandled error: {exceptionObject}");
    }

    private static void LogException(string context, Exception ex)
    {
        WriteLog($"{context}: {ex}");
    }

    private static void WriteLog(string message)
    {
        try
        {
            var line = $"{DateTime.UtcNow:O} {message}{Environment.NewLine}";
            File.AppendAllText(GetLogPath(), line);
        }
        catch
        {
            // Best-effort logging only.
        }
    }

    private static string GetLogPath()
    {
        return Path.Combine(Path.GetTempPath(), "MaiPilot-startup.log");
    }
}
