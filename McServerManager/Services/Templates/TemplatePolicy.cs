namespace McServerManager.Services.Templates;

/// <summary>Application-owned limits. A manifest cannot override them.</summary>
public static class TemplatePolicy
{
    public const int MaxManifestBytes = 1024 * 1024;
    public const int MaxJsonDepth = 16;
    public const int MaxAddons = 256;
    public const int MaxDependencyDepth = 16;
    public const long MaxAddonBytes = 512L * 1024 * 1024;
    public const long MaxTotalAddonBytes = 2L * 1024 * 1024 * 1024;
    public const int MaxRuntimeVersionLength = 96;
}
