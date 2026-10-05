namespace McServerManager.Models.Transactions;

public enum StagedTreeEntryKind { File, Directory }

/// <summary>
/// Reported content of one relative entry, excluding the stage root. Directories have size zero and no hash.
/// This describes content only: it is not a file identity, ownership receipt, or proof of safe filesystem traversal.
/// </summary>
public sealed record StagedTreeEntry(string RelativePath, StagedTreeEntryKind Kind, long SizeBytes, string? Sha256);

/// <summary>Content comparison only. Even ContentMatches grants no publication, cleanup, or recovery authority.</summary>
public enum StagedTreeManifestComparison
{
    InvalidSnapshot, InvalidPlan, InvalidEntry, DuplicateEntry,
    UnexpectedEntry, MissingEntry, ContentMismatch, ContentMatches
}
