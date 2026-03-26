using System.IO;
using System.Threading;
using System.Windows;
using Microsoft.Extensions.DependencyInjection;
using McServerManager.Models;
using McServerManager.Services;
using McServerManager.ViewModels;
using McServerManager.Views;

namespace McServerManager;

public partial class App : System.Windows.Application
{
    private static int _isHandlingDispatcherException;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        RegisterExceptionLogging();
        EnsureWindowsEnvironmentForWpf();

        try
        {
            var provider = ConfigureServices();

            var settingsService = provider.GetRequiredService<IAppSettingsService>();
            var themeService = provider.GetRequiredService<IThemeService>();
            var appSettings = settingsService.Load();
            themeService.Apply(appSettings.Theme);

            var mainWindow = new MainWindow
            {
                DataContext = provider.GetRequiredService<MainViewModel>()
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
            try
            {
                System.Windows.MessageBox.Show(
                    $"Startup failed. Log: {GetLogPath()}",
                    "MaiPilot",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
            catch
            {
                // Avoid throwing while handling startup failure.
            }
            Shutdown(-1);
        }
    }

    private static ServiceProvider ConfigureServices()
    {
        var services = new ServiceCollection();

        // インフラ層 — シングルトン
        services.AddSingleton<AppPathsService>();
        services.AddSingleton<IAppSettingsService, AppSettingsService>();
        services.AddSingleton<IServerConfigService, ServerConfigService>();
        services.AddSingleton<IServerPropertiesService, ServerPropertiesService>();
        services.AddSingleton<IMinecraftVersionService, MinecraftVersionService>();
        services.AddSingleton<IServerRuntimeManager, ServerRuntimeManager>();
        services.AddSingleton<IWorldService, WorldService>();
        services.AddSingleton<IWorldMapService, WorldMapService>();
        services.AddSingleton<IPermissionsService, PermissionsService>();
        services.AddSingleton<IFirewallService, FirewallService>();
        services.AddSingleton<INetworkService, NetworkService>();
        services.AddSingleton<IUpnpService, UpnpService>();
        services.AddSingleton<IDialogService, DialogService>();
        services.AddSingleton<IThemeService, ThemeService>();
        services.AddSingleton<IJavaService, JavaService>();
        services.AddSingleton<IAddonManagementService, AddonManagementService>();
        services.AddSingleton<IAddonCatalogService, AddonCatalogService>();
        services.AddSingleton<IAppUpdateService, AppUpdateService>();
        services.AddSingleton<IServerJarService, ServerJarService>();
        services.AddSingleton<IServerProvisioningService, ServerProvisioningService>();

        // 後方互換: 既存の ViewModel が AppServices を受け取る間は維持
        services.AddSingleton<AppServices>();

        // プレゼンテーション層
        services.AddTransient<MainViewModel>();

        return services.BuildServiceProvider();
    }

    private void ShowFirstRunGuideIfNeeded(IAppSettingsService settingsService, MainWindow mainWindow)
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
                if (Interlocked.Exchange(ref _isHandlingDispatcherException, 1) == 1)
                {
                    return;
                }

                try
                {
                    if (args.Exception is not null)
                    {
                        LogException("Dispatcher", args.Exception);
                    }
                    args.Handled = true;

                    System.Windows.MessageBox.Show(
                        $"Unexpected error. Log: {GetLogPath()}",
                        "MaiPilot",
                        MessageBoxButton.OK,
                        MessageBoxImage.Error);

                    Current?.Shutdown(-1);
                }
                catch (Exception ex)
                {
                    // Avoid throwing from the global exception handler itself.
                    LogException("DispatcherHandler", ex);
                }
                finally
                {
                    Interlocked.Exchange(ref _isHandlingDispatcherException, 0);
                }
            };
        }

        TaskScheduler.UnobservedTaskException += (_, args) =>
        {
            LogException("TaskScheduler", args.Exception);
            args.SetObserved();
        };
    }

    private static void EnsureWindowsEnvironmentForWpf()
    {
        EnsureWindowsPathEnvironmentVariable("windir");
        EnsureWindowsPathEnvironmentVariable("SystemRoot");
        EnsurePathEnvironmentVariable(
            "USERPROFILE",
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)
        );
        EnsurePathEnvironmentVariable(
            "APPDATA",
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData)
        );
        EnsurePathEnvironmentVariable(
            "LOCALAPPDATA",
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData)
        );
        EnsureTempEnvironmentVariable("TEMP");
        EnsureTempEnvironmentVariable("TMP");
    }

    private static void EnsureWindowsPathEnvironmentVariable(string variableName)
    {
        var current = Environment.GetEnvironmentVariable(variableName);
        if (LooksLikeValidExistingWindowsPath(current))
        {
            return;
        }

        var windowsPath = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
        if (!LooksLikeValidExistingWindowsPath(windowsPath))
        {
            return;
        }

        Environment.SetEnvironmentVariable(variableName, windowsPath);
    }

    private static void EnsurePathEnvironmentVariable(string variableName, string fallbackPath)
    {
        var current = Environment.GetEnvironmentVariable(variableName);
        if (LooksLikeValidExistingWindowsPath(current))
        {
            return;
        }

        if (!LooksLikeValidExistingWindowsPath(fallbackPath))
        {
            return;
        }

        Environment.SetEnvironmentVariable(variableName, fallbackPath);
    }

    private static void EnsureTempEnvironmentVariable(string variableName)
    {
        var current = Environment.GetEnvironmentVariable(variableName);
        if (LooksLikeValidExistingWindowsPath(current))
        {
            return;
        }

        var basePath = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        if (!LooksLikeValidExistingWindowsPath(basePath))
        {
            return;
        }

        var tempPath = Path.Combine(basePath, "Temp");
        try
        {
            Directory.CreateDirectory(tempPath);
            Environment.SetEnvironmentVariable(variableName, tempPath);
        }
        catch
        {
            // Ignore if temp path cannot be prepared.
        }
    }

    private static bool LooksLikeValidExistingWindowsPath(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        try
        {
            var fullPath = Path.GetFullPath(value);
            return Path.IsPathRooted(fullPath) && Directory.Exists(fullPath);
        }
        catch
        {
            return false;
        }
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
