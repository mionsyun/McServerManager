using McServerManager.Models.Transactions;

namespace McServerManager.Services.Transactions;

/// <summary>
/// Security boundary, not a generic filesystem abstraction. Implementations must provide all guarantees below.
/// This MVP admits ONLY explicitly isolated synthetic test stores. No production/native implementation exists.
/// </summary>
public interface ITransactionStorage
{
    TransactionStorageCapabilities Capabilities { get; }

    /// <summary>
    /// Query the installId journal. Published is sticky and bound to installId, storageId, fingerprint and recorded
    /// target identity, independently of attempt ownerToken (replays generate new owners). OwnedStage/cleanup
    /// require the exact original ownerToken. All stage/publish/cleanup operations must serialize per installId;
    /// an unrelated/changed fingerprint is ConflictingPlan. Unknown is required whenever commit cannot be proven.
    /// NotStarted proves no stage or publication belonging to this attempt exists. Never infer it from target absence alone.
    /// </summary>
    Task<TransactionObservation> ObserveAsync(TransactionReceipt receipt, CancellationToken cancellationToken);

    /// <summary>
    /// Exclusively reserve installId and create a new owned complete-tree stage on the target volume, outside ALL
    /// server-discovery roots. Retain stable anchor/stage identities; reject symlinks/reparse points, aliases and
    /// foreign entries. Bind the receipt before creating anything, so exceptions remain reconcilable by installId.
    /// An existing target, changed anchor/baseline, or nonexclusive/stale stopped lease must fail closed.
    /// </summary>
    Task CreateStageAsync(TransactionReceipt receipt, TransactionPlan plan, CancellationToken cancellationToken);

    /// <summary>Create-only write under the owned stage; never follow links or overwrite anything, including partial files.</summary>
    Task StageArtifactAsync(TransactionReceipt receipt, TransactionArtifact artifact, ReadOnlyMemory<byte> bytes,
        CancellationToken cancellationToken);

    /// <summary>
    /// Independently verify the EXACT full staged tree, sizes and hashes (including absence of foreign entries).
    /// Atomically hold a valid stopped/start-exclusion lease, revalidate anchor identity/baseline and absent target,
    /// and publish the same verified directory with no replacement. Freeze staged bytes until the linearization point.
    /// Ignore cancellation after publication; journal/ObserveAsync must report Published even if this method throws afterward.
    /// If any outcome is uncertain, ObserveAsync MUST return Unknown and cleanup must refuse.
    /// Windows path checks + Directory.Move alone do not satisfy this contract against hostile concurrent mutations.
    /// </summary>
    Task<TransactionPublishDecision> PublishAsync(TransactionReceipt receipt, TransactionPlan plan,
        CancellationToken cancellationToken);

    /// <summary>
    /// Remove only the identity-proven owned unpublished stage and exact journaled bytes/tree. Refuse ANY changed,
    /// foreign or linked entry and uncertain publication. Never delete/mutate the final target. This is safe to retry
    /// only using the same ownership receipt; an already-removed stage is success. Recovery is not recursive path deletion.
    /// </summary>
    Task<TransactionCleanupDecision> CleanupOwnedStageAsync(TransactionReceipt receipt, CancellationToken cancellationToken);
}
