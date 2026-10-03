namespace McServerManager.Models.Templates;

public sealed record TemplateAddon
{
    public required string EntryId { get; init; }
    public required string Kind { get; init; }
    public required string Selection { get; init; }
    public required TemplateAddonSource Source { get; init; }
    public required string Sha256 { get; init; }
    public required long SizeBytes { get; init; }
    public required IReadOnlyList<string> Requires { get; init; }
}

/// <summary>Identifiers for future provider verification, never a path or download URL.</summary>
public sealed record TemplateAddonSource
{
    public required string Provider { get; init; }
    public required string ProjectId { get; init; }
    public string? VersionId { get; init; }
    public string? FileId { get; init; }
    public required string FileName { get; init; }
    public string? Sha512 { get; init; }
    public string? Sha1 { get; init; }
}
