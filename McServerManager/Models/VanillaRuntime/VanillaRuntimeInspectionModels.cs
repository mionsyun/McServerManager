namespace McServerManager.Models.VanillaRuntime;

public enum VanillaRuntimeInspectionStatus { Unsupported, Rejected, Unavailable, Verified }

/// <summary>Evidence of one complete, memory-only verification; never installation or Apply authority.</summary>
public sealed record VanillaRuntimeArtifactEvidence(
    string MinecraftVersion,
    string VersionManifestSha1,
    string DownloadUrl,
    long SizeBytes,
    string Sha1,
    string Sha256);

public sealed record VanillaRuntimeInspectionResult(
    VanillaRuntimeInspectionStatus Status,
    string Code,
    VanillaRuntimeArtifactEvidence? Evidence = null);

public sealed record VanillaRuntimeInspectionProgress(string Stage, long BytesRead = 0, long? TotalBytes = null);
