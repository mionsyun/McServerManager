namespace McServerManager.Services;

public sealed class AppServices
{
    public AppServices(
        AppPathsService paths,
        AppSettingsService settings,
        ServerConfigService configs,
        ServerRuntimeManager runtime,
        ServerPropertiesService properties,
        MinecraftVersionService versions,
        ServerProvisioningService provisioning,
        ServerJarService jars,
        WorldService worlds,
        PermissionsService permissions,
        FirewallService firewall,
        NetworkService network,
        UpnpService upnp,
        IDialogService dialog,
        ThemeService theme,
        JavaService java,
        AddonManagementService addons,
        AddonCatalogService addonCatalog)
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
        Permissions = permissions;
        Firewall = firewall;
        Network = network;
        Upnp = upnp;
        Dialog = dialog;
        Theme = theme;
        Java = java;
        Addons = addons;
        AddonCatalog = addonCatalog;
    }

    public AppPathsService Paths { get; }
    public AppSettingsService Settings { get; }
    public ServerConfigService Configs { get; }
    public ServerRuntimeManager Runtime { get; }
    public ServerPropertiesService Properties { get; }
    public MinecraftVersionService Versions { get; }
    public ServerProvisioningService Provisioning { get; }
    public ServerJarService Jars { get; }
    public WorldService Worlds { get; }
    public PermissionsService Permissions { get; }
    public FirewallService Firewall { get; }
    public NetworkService Network { get; }
    public UpnpService Upnp { get; }
    public IDialogService Dialog { get; }
    public ThemeService Theme { get; }
    public JavaService Java { get; }
    public AddonManagementService Addons { get; }
    public AddonCatalogService AddonCatalog { get; }
}
