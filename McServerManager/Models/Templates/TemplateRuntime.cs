namespace McServerManager.Models.Templates;

public sealed record TemplateRuntime
{
    public required string Type { get; init; }
    public required string MinecraftVersion { get; init; }
    public string? Build { get; init; }
    public string? LoaderVersion { get; init; }
    public string? InstallerVersion { get; init; }
}
