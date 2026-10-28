namespace McServerManager.Models.Dependencies;

public enum DependencyProvider { Modrinth, CurseForge }
public enum DependencySide { Unknown, Client, Server, Universal }
public enum DependencyRelation { Required, Optional, Incompatible, Warning, Embedded }
public enum DependencyEvidenceSource { Unknown, ProviderApi, FabricMetadata }
public enum DependencyConstraintKind { Unknown, Exact, Range }
public enum DependencyEvidenceStatus { Unknown, Verified, Missing, Unsupported, Invalid }
public enum ProvidedModKind { Primary, Alias, Nested }
public enum DependencyPlanAction { Add, Keep, ReplacementRequiresReview }
public enum DependencyDiagnosticSeverity { Warning, Blocker }

/// <summary>An exact catalog file identity. IDs are case-sensitive; digests use lowercase hex.</summary>
public sealed record DependencyArtifactIdentity
{
    public DependencyProvider Provider { get; init; }
    public string ProjectId { get; init; } = "";
    public string VersionId { get; init; } = "";
    public string FileName { get; init; } = "";
    public string Sha256 { get; init; } = "";
    public long SizeBytes { get; init; }
}

/// <summary>Already verified declarations only. This DTO does not verify or inspect archives.</summary>
public sealed record ProvidedModEvidence
{
    public string ModId { get; init; } = "";
    public string Version { get; init; } = "";
    public ProvidedModKind Kind { get; init; }
    public DependencySide Side { get; init; }
    public DependencyEvidenceStatus Status { get; init; }
    // For a primary/alias this must match the inspected parent; nested content has its own digest.
    public string ContentSha256 { get; init; } = "";
}

/// <summary>
/// A normalized declaration. Catalog requirements need an exact Artifact. A textual Fabric
/// mod ID needs an exact version and a verified binding, or an already selected/installed provider.
/// Ranges are retained for diagnostics and deliberately unsupported in this first planner.
/// </summary>
public sealed record DependencyRequirement
{
    public DependencyRelation Relation { get; init; }
    public DependencyEvidenceSource Source { get; init; }
    public DependencyConstraintKind ConstraintKind { get; init; }
    public DependencyArtifactIdentity? Artifact { get; init; }
    public string? ModId { get; init; }
    public string? VersionConstraint { get; init; }
    public string OriginalRelation { get; init; } = "";
}

public sealed record DependencyArtifactEvidence
{
    public DependencyArtifactIdentity Artifact { get; init; } = new();
    public DependencyEvidenceStatus ApiStatus { get; init; }
    public DependencyEvidenceStatus MetadataStatus { get; init; }
    public bool DistributionAllowed { get; init; }
    public string ApiFileSha256 { get; init; } = "";
    public string InspectedContentSha256 { get; init; } = "";
    public long VerifiedSizeBytes { get; init; }
    public string Loader { get; init; } = "";
    public DependencySide Side { get; init; }
    public IReadOnlyList<string> MinecraftVersions { get; init; } = Array.Empty<string>();
    public IReadOnlyList<ProvidedModEvidence> ProvidedMods { get; init; } = Array.Empty<ProvidedModEvidence>();
    // Union of API and JAR declarations; neither source overrides or erases the other.
    public IReadOnlyList<DependencyRequirement> Requirements { get; init; } = Array.Empty<DependencyRequirement>();
}

/// <summary>
/// Injected provenance from a verified receipt/hash lookup/mapping. A display name, filename,
/// slug, unverified candidate choice, or plain mod ID is never such a binding.
/// </summary>
public sealed record VerifiedModBinding
{
    public string ModId { get; init; } = "";
    public string Version { get; init; } = "";
    public DependencyArtifactIdentity Artifact { get; init; } = new();
    public DependencyEvidenceStatus Status { get; init; }
    public string EvidenceContentSha256 { get; init; } = "";
}

public sealed record DependencyPlanningRequest
{
    public string MinecraftVersion { get; init; } = "";
    public string Loader { get; init; } = "fabric";
    public string LoaderVersion { get; init; } = "";
    public string JavaVersion { get; init; } = "";
    public IReadOnlyList<DependencyArtifactIdentity> SelectedArtifacts { get; init; } = Array.Empty<DependencyArtifactIdentity>();
    public IReadOnlyList<DependencyArtifactIdentity> InstalledArtifacts { get; init; } = Array.Empty<DependencyArtifactIdentity>();
    public IReadOnlyList<DependencyArtifactEvidence> Evidence { get; init; } = Array.Empty<DependencyArtifactEvidence>();
    public IReadOnlyList<VerifiedModBinding> ModBindings { get; init; } = Array.Empty<VerifiedModBinding>();
}

public sealed record DependencyPlanReason
{
    public DependencyEvidenceSource Source { get; init; }
    public string Message { get; init; } = "";
    public IReadOnlyList<DependencyArtifactIdentity> Chain { get; init; } = Array.Empty<DependencyArtifactIdentity>();
}

public sealed record DependencyPlanItem
{
    public DependencyArtifactIdentity Artifact { get; init; } = new();
    public DependencyPlanAction Action { get; init; }
    public DependencyArtifactIdentity? InstalledArtifact { get; init; }
    public IReadOnlyList<DependencyPlanReason> Reasons { get; init; } = Array.Empty<DependencyPlanReason>();
}

public sealed record DependencyOptionalSuggestion
{
    public DependencyArtifactIdentity Parent { get; init; } = new();
    public DependencyRequirement Requirement { get; init; } = new();
    public bool IsSelected { get; init; }
}

public sealed record DependencyDiagnostic
{
    public string Code { get; init; } = "";
    public DependencyDiagnosticSeverity Severity { get; init; }
    public string Message { get; init; } = "";
    public IReadOnlyList<DependencyArtifactIdentity> Chain { get; init; } = Array.Empty<DependencyArtifactIdentity>();
}

/// <summary>A review-only result. This is not an install authorization or a claim of runtime safety.</summary>
public sealed record DependencyPlan
{
    public bool IsResolved { get; init; }
    public string Assurance { get; init; } = "";
    public IReadOnlyList<DependencyPlanItem> Items { get; init; } = Array.Empty<DependencyPlanItem>();
    public IReadOnlyList<DependencyOptionalSuggestion> OptionalSuggestions { get; init; } = Array.Empty<DependencyOptionalSuggestion>();
    public IReadOnlyList<DependencyDiagnostic> Diagnostics { get; init; } = Array.Empty<DependencyDiagnostic>();
}
