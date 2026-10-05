using System.Collections.Immutable;
using McServerManager.Models.Transactions;

namespace McServerManager.Services.Transactions;

/// <summary>Conservative cross-platform lexical rules; they do NOT make path-based filesystem I/O race-safe.</summary>
public static class TransactionPolicy
{
    public const int MaxArtifacts = 64;
    public const int MaxPathDepth = 8;
    public const int MaxArtifactBytes = 1024 * 1024;
    public const long MaxTotalBytes = 16L * 1024 * 1024;
    public const int MaxSettings = 32;

    public static void Validate(TransactionPlan plan)
    {
        ArgumentNullException.ThrowIfNull(plan);
        if (plan.InstallId == Guid.Empty) throw new ArgumentException("InstallId must be nonempty.");
        ArgumentNullException.ThrowIfNull(plan.Target);
        ArgumentNullException.ThrowIfNull(plan.Review);
        foreach (var id in new[] { plan.Target.StorageId, plan.Target.AnchorIdentity, plan.Target.ServerIdentity,
                     plan.Target.AbsentBaseline, plan.Target.StoppedLease }) ValidateIdentity(id);
        ValidatePath(plan.Target.TargetName, singleSegment: true);
        ValidateSha256(plan.Review.SourceManifestSha256);
        ValidateSha256(plan.Review.InspectionEvidenceSha256);
        if (plan.Review.RuntimeIdentity != "synthetic") throw new ArgumentException("Only the synthetic runtime is supported.");
        ValidateIdentity(plan.Review.RuntimeVersion);
        if (plan.Review.MemoryMiB is < 512 or > 32768 || plan.Review.Port is < 1 or > 65535)
            throw new ArgumentException("Memory or port is outside policy bounds.");
        ValidateSettings(plan.Review.Settings);
        if (plan.Artifacts.IsDefault || plan.Artifacts.Length is < 1 or > MaxArtifacts)
            throw new ArgumentException("The artifact count is outside policy bounds.");
        long total = 0;
        var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var identities = new HashSet<(string, string, string, string)>();
        var directories = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var artifact in plan.Artifacts)
        {
            ArgumentNullException.ThrowIfNull(artifact);
            if (artifact.Provider != "synthetic") throw new ArgumentException("Only synthetic payloads are supported.");
            ValidateIdentity(artifact.ProjectId);
            ValidateIdentity(artifact.VersionId);
            ValidateIdentity(artifact.ArtifactId);
            ValidatePath(artifact.Destination);
            ValidateSha256(artifact.Sha256);
            if (artifact.SizeBytes is < 1 or > MaxArtifactBytes) throw new ArgumentException("Artifact size is outside policy bounds.");
            total = checked(total + artifact.SizeBytes);
            if (total > MaxTotalBytes) throw new ArgumentException("Total payload size exceeds policy bounds.");
            // A Windows directory has one spelling in the approved namespace. Distinct files must
            // not implicitly request both "Mods" and "mods", even on a case-sensitive test host.
            for (var slash = artifact.Destination.IndexOf('/'); slash >= 0; slash = artifact.Destination.IndexOf('/', slash + 1))
            {
                var directory = artifact.Destination[..slash];
                if (directories.TryGetValue(directory, out var spelling) && spelling != directory)
                    throw new ArgumentException("Inconsistent directory casing in destinations.");
                directories[directory] = directory;
            }
            if (!paths.Add(artifact.Destination)) throw new ArgumentException("Case-insensitive destination collision.");
            if (!identities.Add((artifact.Provider, artifact.ProjectId, artifact.VersionId, artifact.ArtifactId)))
                throw new ArgumentException("Duplicate artifact identity.");
        }
        foreach (var path in paths)
        {
            for (var slash = path.IndexOf('/'); slash >= 0; slash = path.IndexOf('/', slash + 1))
                if (paths.Contains(path[..slash])) throw new ArgumentException("File/directory destination collision.");
        }
    }

    public static void ValidateIdentity(string value)
    {
        if (string.IsNullOrEmpty(value) || value.Length > 96 ||
            value.Any(c => !char.IsAsciiLetterOrDigit(c) && c is not '-' and not '_' and not '.'))
            throw new ArgumentException("Identity must be a bounded ASCII token.");
    }

    public static void ValidateSha256(string value)
    {
        if (value is null || value.Length != 64 || value.Any(c => c is not (>= '0' and <= '9') and not (>= 'a' and <= 'f')))
            throw new ArgumentException("SHA-256 must be 64 lowercase hexadecimal characters.");
    }

    public static void ValidatePath(string value, bool singleSegment = false)
    {
        if (string.IsNullOrEmpty(value) || value.Length > 240) throw new ArgumentException("Invalid relative destination.");
        var segments = value.Split('/');
        if (segments.Length > MaxPathDepth || singleSegment && segments.Length != 1) throw new ArgumentException("Invalid path depth.");
        foreach (var segment in segments)
        {
            if (segment.Length is < 1 or > 80 || !char.IsAsciiLetterOrDigit(segment[0]) || segment.EndsWith('.') ||
                segment.Any(c => !char.IsAsciiLetterOrDigit(c) && c is not '-' and not '_' and not '.'))
                throw new ArgumentException("Unsafe path segment, alias, separator, stream, or traversal.");
            var stem = segment.Split('.')[0].ToUpperInvariant();
            if (stem is "CON" or "PRN" or "AUX" or "NUL" ||
                stem.Length == 4 && (stem.StartsWith("COM", StringComparison.Ordinal) || stem.StartsWith("LPT", StringComparison.Ordinal)) && stem[3] is >= '0' and <= '9')
                throw new ArgumentException("Windows device names are forbidden on every platform.");
            if (segment.Equals("config.json", StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException("Synthetic transactions cannot create discoverable server configuration.");
        }
    }

    private static void ValidateSettings(ImmutableArray<TransactionSetting> settings)
    {
        if (settings.IsDefault || settings.Length > MaxSettings) throw new ArgumentException("Invalid settings count.");
        var keys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var setting in settings)
        {
            ArgumentNullException.ThrowIfNull(setting);
            ValidateIdentity(setting.Key);
            if (!keys.Add(setting.Key)) throw new ArgumentException("Duplicate setting key.");
            if (setting.Value is null || setting.Value.Length > 256 || setting.Value.Any(c => c is < ' ' or > '~'))
                throw new ArgumentException("Settings must be bounded printable ASCII.");
        }
    }
}
