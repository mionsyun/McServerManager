using System.Collections.Immutable;
using McServerManager.Models.Transactions;

namespace McServerManager.Services.Transactions;

/// <summary>
/// Pure, bounded comparison of a complete reported namespace against an approved synthetic plan.
/// Does not read files, authenticate the snapshot, verify native identities/links/streams, freeze bytes,
/// reserve install IDs, acquire stopped leases, publish, or delete. A storage adapter must independently
/// establish those guarantees and collect fresh hashes from the SAME pinned objects it will publish.
/// </summary>
public static class StagedTreeManifestVerifier
{
    public const int MaxEntries = TransactionPolicy.MaxArtifacts * TransactionPolicy.MaxPathDepth;

    public static StagedTreeManifestComparison Compare(TransactionPlan plan, ImmutableArray<StagedTreeEntry> snapshot)
    {
        try { TransactionPolicy.Validate(plan); }
        catch (ArgumentException) { return StagedTreeManifestComparison.InvalidPlan; }
        // Do not enumerate caller code, open paths, or allocate a collection from an unbounded sequence.
        if (snapshot.IsDefault || snapshot.Length > MaxEntries)
            return StagedTreeManifestComparison.InvalidSnapshot;

        var expected = new Dictionary<string, StagedTreeEntry>(StringComparer.Ordinal);
        foreach (var artifact in plan.Artifacts)
        {
            expected.Add(artifact.Destination, new(artifact.Destination, StagedTreeEntryKind.File, artifact.SizeBytes, artifact.Sha256));
            for (var slash = artifact.Destination.IndexOf('/'); slash >= 0; slash = artifact.Destination.IndexOf('/', slash + 1))
            {
                var directory = artifact.Destination[..slash];
                expected.TryAdd(directory, new(directory, StagedTreeEntryKind.Directory, 0, null));
            }
        }

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in snapshot)
        {
            if (entry is null) return StagedTreeManifestComparison.InvalidEntry;
            try
            {
                TransactionPolicy.ValidatePath(entry.RelativePath);
                switch (entry.Kind)
                {
                    case StagedTreeEntryKind.File:
                        TransactionPolicy.ValidateSha256(entry.Sha256!);
                        if (entry.SizeBytes is < 1 or > TransactionPolicy.MaxArtifactBytes)
                            return StagedTreeManifestComparison.InvalidEntry;
                        break;
                    case StagedTreeEntryKind.Directory:
                        if (entry.SizeBytes != 0 || entry.Sha256 is not null)
                            return StagedTreeManifestComparison.InvalidEntry;
                        break;
                    default: return StagedTreeManifestComparison.InvalidEntry;
                }
            }
            catch (ArgumentException) { return StagedTreeManifestComparison.InvalidEntry; }
            if (!seen.Add(entry.RelativePath)) return StagedTreeManifestComparison.DuplicateEntry;
            if (!expected.TryGetValue(entry.RelativePath, out var approved))
                return StagedTreeManifestComparison.UnexpectedEntry;
            if (entry != approved) return StagedTreeManifestComparison.ContentMismatch;
        }
        return seen.Count == expected.Count
            ? StagedTreeManifestComparison.ContentMatches : StagedTreeManifestComparison.MissingEntry;
    }
}
