namespace McServerManager.Services;

/// <summary>
/// サービスロケーター（後方互換のために維持。新規コードでは直接DIを使うこと）。
/// </summary>
public sealed class AppServices
{
    public AppServices(
        AppPathsService paths,
        IAppSettingsService settings,
        IServerConfigService configs,
        IServerRuntimeManager runtime,
        IServerPropertiesService properties,
        IMinecraftVersionService versions,
        IServerProvisioningService provisioning,
        IServerJarService jars,
        IWorldService worlds,
        IWorldMapService worldMap,
        IPermissionsService permissions,
        IFirewallService firewall,
        INetworkService network,
        IUpnpService upnp,
        IDialogService dialog,
        IThemeService theme,
        IJavaService java,
        IAddonManagementService addons,
        IAddonCatalogService addonCatalog,
        IAppUpdateService appUpdate)
    {
        Paths = paths;
        Settings = settings;
        Configs = configs;
        Runtime = runtime;
        Properties = properties;
        Versions = versions;
        Provisioning = provisioning;
        Jars = jars;
        Worlds = worlds;
        WorldMap = worldMap;
        Permissions = permissions;
        Firewall = firewall;
        Network = network;
        Upnp = upnp;
        Dialog = dialog;
        Theme = theme;
        Java = java;
        Addons = addons;
        AddonCatalog = addonCatalog;
        AppUpdate = appUpdate;
    }

    public AppPathsService Paths { get; }
    public IAppSettingsService Settings { get; }
    public IServerConfigService Configs { get; }
    public IServerRuntimeManager Runtime { get; }
    public IServerPropertiesService Properties { get; }
    public IMinecraftVersionService Versions { get; }
    public IServerProvisioningService Provisioning { get; }
    public IServerJarService Jars { get; }
    public IWorldService Worlds { get; }
    public IWorldMapService WorldMap { get; }
    public IPermissionsService Permissions { get; }
    public IFirewallService Firewall { get; }
    public INetworkService Network { get; }
    public IUpnpService Upnp { get; }
    public IDialogService Dialog { get; }
    public IThemeService Theme { get; }
    public IJavaService Java { get; }
    public IAddonManagementService Addons { get; }
    public IAddonCatalogService AddonCatalog { get; }
    public IAppUpdateService AppUpdate { get; }
}
