using McServerManager.Models.Fabric;

namespace McServerManager.Models.Modrinth;

/// <summary>A user pin. No caller-supplied network location is accepted.</summary>
public sealed record ModrinthPin
{
    public string VersionId { get; init; } = "";
    public string? ProjectId { get; init; }
    public string? FileName { get; init; }
    public string? Sha256 { get; init; }
    public string? Sha512 { get; init; }
    public string? Sha1 { get; init; }
    public long? SizeBytes { get; init; }
}

public sealed record ModrinthProject(string Id, string Title, string Status, string ServerSide, string ApiUrl);
public sealed record ModrinthFile(string FileName, string Url, long SizeBytes, string Sha512, string? Sha1, bool Primary);
public sealed record ModrinthDependency(string Type, string? ProjectId, string? VersionId, string? FileName);
public sealed record ModrinthVersion(string Id, string ProjectId, string VersionNumber, string Status,
    IReadOnlyList<string> GameVersions, IReadOnlyList<string> Loaders, IReadOnlyList<ModrinthFile> Files,
    IReadOnlyList<ModrinthDependency> Dependencies, string? Environment, string ApiUrl);
public sealed record ModrinthDownload(byte[] Bytes, string Sha256, string Sha512, string Sha1);

public sealed record ModrinthInspectionRequest
{
    public string MinecraftVersion { get; init; } = "";
    public string LoaderVersion { get; init; } = "";
    public string JavaVersion { get; init; } = "";
    public IReadOnlyList<ModrinthPin> SelectedPins { get; init; } = Array.Empty<ModrinthPin>();
    // These pins authorize resolving project-only API edges, never selecting the latest release.
    public IReadOnlyList<ModrinthPin> DependencyPins { get; init; } = Array.Empty<ModrinthPin>();
    public IReadOnlyList<string> SelectedLocalFiles { get; init; } = Array.Empty<string>();
    public IReadOnlyList<string> InstalledLocalFiles { get; init; } = Array.Empty<string>();
}

public enum ModrinthFindingSeverity { Information, Warning, Blocker }
public sealed record ModrinthFinding
{
    public string Code { get; init; } = "";
    public ModrinthFindingSeverity Severity { get; init; }
    public string Message { get; init; } = "";
    public string Source { get; init; } = "";
    public string? ArtifactVersionId { get; init; }
    public string? Dependency { get; init; }
    public IReadOnlyList<string> Chain { get; init; } = Array.Empty<string>();
}
public sealed record ModrinthInspectedArtifact
{
    public ModrinthVersion? Version { get; init; }
    public ModrinthProject? Project { get; init; }
    public ModrinthFile? File { get; init; }
    public string SourceName { get; init; } = "";
    public string Sha256 { get; init; } = "";
    public string Sha512 { get; init; } = "";
    public string Sha1 { get; init; } = "";
    public long SizeBytes { get; init; }
    public bool IsInstalled { get; init; }
    public bool IsTransitive { get; init; }
    public bool ProviderVerified { get; init; }
    public FabricJarInspection Metadata { get; init; } = new();
    public IReadOnlyList<string> Chain { get; init; } = Array.Empty<string>();
}
public sealed record ModrinthInspectionResult
{
    public bool IsResolved { get; init; }
    public string Assurance { get; init; } = "Read-only metadata review. No MOD was installed or executed, and no server was started. Matching declarations do not establish runtime safety.";
    public IReadOnlyList<ModrinthInspectedArtifact> Artifacts { get; init; } = Array.Empty<ModrinthInspectedArtifact>();
    public IReadOnlyList<ModrinthFinding> Findings { get; init; } = Array.Empty<ModrinthFinding>();
}
