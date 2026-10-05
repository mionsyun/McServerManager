using McServerManager.Models.Templates;

namespace McServerManager.Models.Authoring;

/// <summary>Only explicit, portable declarations. No server capture, destination or executable input.</summary>
public sealed record TemplateAuthoringDraft
{
    public string Name { get; init; } = "";
    public string Description { get; init; } = "";
    public TemplateRuntime Runtime { get; init; } = new() { Type = "vanilla", MinecraftVersion = "" };
    public TemplateSettings Settings { get; init; } = new();
    public TemplateAuthoringClientDraft? ClientDefinition { get; init; }
}

/// <summary>An optional client declaration supplied by the author, never inferred from server addons.</summary>
public sealed record TemplateAuthoringClientDraft
{
    public string Name { get; init; } = "";
    public string MinecraftVersion { get; init; } = "";
    public string Loader { get; init; } = "fabric";
    public string LoaderVersion { get; init; } = "";
    public string JavaVersion { get; init; } = "";
    public IReadOnlyList<TemplateAuthoringClientModDraft> Mods { get; init; } = Array.Empty<TemplateAuthoringClientModDraft>();
}

public sealed record TemplateAuthoringClientModDraft
{
    public string Provider { get; init; } = "modrinth";
    public string ProjectId { get; init; } = "";
    public string VersionId { get; init; } = "";
    public string Name { get; init; } = "";
    public string Version { get; init; } = "";
    public string ClientSide { get; init; } = "required";
    public string? Note { get; init; }
}
