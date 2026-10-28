using McServerManager.Models.Participants;

namespace McServerManager.Models.Templates;

/// <summary>Portable, data-only manifest. A parsed manifest is not an installation authorization.</summary>
public sealed record TemplateManifest
{
    public required string SchemaVersion { get; init; }
    public required Guid TemplateId { get; init; }
    public required int Revision { get; init; }
    public required string Name { get; init; }
    public required string Description { get; init; }
    public required string Edition { get; init; }
    public required TemplateRuntime Runtime { get; init; }
    public required TemplateSettings Settings { get; init; }
    public required IReadOnlyList<TemplateAddon> Addons { get; init; }
    /// <summary>Explicit, input-validated client declaration, or null when absent. Never inferred from server addons.</summary>
    public ParticipantClientDefinition? ClientDefinition { get; init; }
}
