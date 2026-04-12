using System.Diagnostics;
using System.Windows;
using McServerManager.Models;
using McServerManager.Services;

namespace McServerManager.Tests.TestInfrastructure;

internal sealed class TestDialogService : IDialogService
{
    public Queue<MessageBoxResult> QueuedResults { get; } = new();
    public List<(string Message, string Title, MessageBoxButton Buttons, MessageBoxImage Image)> Calls { get; } = [];
    public MessageBoxResult DefaultResult { get; set; } = MessageBoxResult.Yes;

    public MessageBoxResult Show(
        string message,
        string title,
        MessageBoxButton buttons = MessageBoxButton.OK,
        MessageBoxImage image = MessageBoxImage.None)
    {
        Calls.Add((message, title, buttons, image));
        return QueuedResults.Count > 0 ? QueuedResults.Dequeue() : DefaultResult;
    }
}

internal sealed class StubMinecraftVersionService : IMinecraftVersionService
{
    public Task<IReadOnlyList<MinecraftVersionInfo>> GetVersionsAsync(bool forceRefresh = false)
        => Task.FromResult<IReadOnlyList<MinecraftVersionInfo>>(Array.Empty<MinecraftVersionInfo>());

    public Task DownloadServerJarAsync(string versionId, string destinationPath)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(destinationPath)!);
        File.WriteAllText(destinationPath, $"stub-version:{versionId}");
        return Task.CompletedTask;
    }
}

internal sealed class StubServerProvisioningService : IServerProvisioningService
{
    public Task<ServerConfig> CreateAsync(NewServerOptions options, IProgress<string>? progress = null)
        => throw new NotSupportedException("Not used in this test.");
}

internal sealed class StubFirewallService : IFirewallService
{
    public bool IsAdministrator() => true;

    public FirewallRuleInfo BuildRuleInfo(string serverId) => new()
    {
        TcpRuleName = $"stub_{serverId}_tcp",
        UdpRuleName = $"stub_{serverId}_udp"
    };

    public void CreateRules(int port, FirewallRuleInfo info)
    {
    }

    public void DeleteRules(FirewallRuleInfo info)
    {
    }

    public void RecreateRules(int port, FirewallRuleInfo info)
    {
    }
}

internal sealed class StubNetworkService : INetworkService
{
    public IReadOnlyList<string> GetLanIpAddresses() => Array.Empty<string>();
    public IReadOnlyList<string> GetExternalChecklist() => Array.Empty<string>();
    public Task<string?> GetPublicIpAsync() => Task.FromResult<string?>(null);
    public IReadOnlyList<Process> GetProcessesUsingPort(int port) => Array.Empty<Process>();
    public bool TryKillProcessesUsingPort(int port, out string? error)
    {
        error = null;
        return true;
    }
}

internal sealed class StubUpnpService : IUpnpService
{
    public Task<(bool ok, string? error)> TryOpenPortAsync(int port, string description)
        => Task.FromResult((true, (string?)null));

    public Task TryClosePortAsync(int port) => Task.CompletedTask;
}

internal sealed class StubThemeService : IThemeService
{
    public bool IsDark(string? theme) => string.Equals(theme, "Dark", StringComparison.OrdinalIgnoreCase);
    public string Normalize(string? theme) => string.IsNullOrWhiteSpace(theme) ? "Dark" : theme!;
    public void Apply(string? theme)
    {
    }
}

internal sealed class StubJavaService : IJavaService
{
    public string? FindJavaExecutable() => "java";

    public bool TryGetJavaMajorVersion(string javaExe, out int major, out string? rawVersion)
    {
        major = 21;
        rawVersion = "21.0.1";
        return true;
    }

    public int? GetRequiredJavaMajor(string? minecraftVersion) => 17;
}

internal sealed class StubAddonCatalogService : IAddonCatalogService
{
    private readonly IReadOnlyList<AddonSearchResult> _results;

    public StubAddonCatalogService(IReadOnlyList<AddonSearchResult>? results = null)
    {
        _results = results ?? Array.Empty<AddonSearchResult>();
    }

    public Task<IReadOnlyList<AddonSearchResult>> SearchAsync(string query, string serverType, string minecraftVersion, int limit = 20)
        => Task.FromResult(_results);
}

internal sealed class StubAppUpdateService : IAppUpdateService
{
    public Task<AppUpdateCheckResult> CheckForUpdatesAsync(CancellationToken cancellationToken = default)
        => Task.FromResult(AppUpdateCheckResult.UpToDate());

    public Task<AppUpdateDownloadResult> DownloadAndLaunchInstallerAsync(AppUpdateManifest manifest, CancellationToken cancellationToken = default)
        => Task.FromResult(AppUpdateDownloadResult.DownloadFailed("stub"));
}

internal static class TestAppServicesFactory
{
    public static AppServices CreateForAddonTests(
        AppPathsService paths,
        IDialogService dialog,
        IAddonManagementService? addonManagement = null,
        IAddonCatalogService? addonCatalog = null)
    {
        var settings = new AppSettingsService(paths);
        var configs = new ServerConfigService(paths);
        var runtime = new ServerRuntimeManager();
        var properties = new ServerPropertiesService();
        var versions = new StubMinecraftVersionService();
        var provisioning = new StubServerProvisioningService();
        var jars = new FakeServerJarService();
        var worlds = new WorldService();
        var worldMap = new WorldMapService();
        var permissions = new PermissionsService();
        var firewall = new StubFirewallService();
        var network = new StubNetworkService();
        var upnp = new StubUpnpService();
        var theme = new StubThemeService();
        var java = new StubJavaService();
        var addons = addonManagement ?? new AddonManagementService();
        var catalog = addonCatalog ?? new StubAddonCatalogService();
        var appUpdate = new StubAppUpdateService();

        return new AppServices(
            paths,
            settings,
            configs,
            runtime,
            properties,
            versions,
            provisioning,
            jars,
            worlds,
            worldMap,
            permissions,
            firewall,
            network,
            upnp,
            dialog,
            theme,
            java,
            addons,
            catalog,
            appUpdate);
    }
}
