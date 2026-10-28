namespace McServerManager.Models.Fabric;

public enum FabricEnvironment { Universal, Client, Server, Unknown }
public enum FabricVersionMatch { Match, NoMatch, Unknown }

/// <summary>Alternatives are OR; space-separated terms inside each alternative are AND.</summary>
public sealed record FabricVersionConstraint
{
    public IReadOnlyList<string> Alternatives { get; init; } = Array.Empty<string>();
}

public sealed record FabricModMetadata
{
    public string Id { get; init; } = "";
    public string Version { get; init; } = "";
    public FabricEnvironment Environment { get; init; }
    public string ArchivePath { get; init; } = "";
    public string ContentSha256 { get; init; } = "";
    public int NestedDepth { get; init; }
    public IReadOnlyList<string> Provides { get; init; } = Array.Empty<string>();
    public IReadOnlyList<string> NestedJars { get; init; } = Array.Empty<string>();
    public IReadOnlyDictionary<string, FabricVersionConstraint> Depends { get; init; } = new Dictionary<string, FabricVersionConstraint>();
    public IReadOnlyDictionary<string, FabricVersionConstraint> Recommends { get; init; } = new Dictionary<string, FabricVersionConstraint>();
    public IReadOnlyDictionary<string, FabricVersionConstraint> Suggests { get; init; } = new Dictionary<string, FabricVersionConstraint>();
    public IReadOnlyDictionary<string, FabricVersionConstraint> Breaks { get; init; } = new Dictionary<string, FabricVersionConstraint>();
    public IReadOnlyDictionary<string, FabricVersionConstraint> Conflicts { get; init; } = new Dictionary<string, FabricVersionConstraint>();
}

public sealed record FabricInspectionIssue
{
    public string Code { get; init; } = "";
    public string Message { get; init; } = "";
    public string ArchivePath { get; init; } = "";
}

/// <summary>All issues are blockers. Partial metadata is evidence only, never a successful inspection.</summary>
public sealed record FabricJarInspection
{
    public bool IsComplete { get; init; }
    public FabricModMetadata? Root { get; init; }
    public IReadOnlyList<FabricModMetadata> Mods { get; init; } = Array.Empty<FabricModMetadata>();
    public IReadOnlyList<FabricInspectionIssue> Issues { get; init; } = Array.Empty<FabricInspectionIssue>();
}
