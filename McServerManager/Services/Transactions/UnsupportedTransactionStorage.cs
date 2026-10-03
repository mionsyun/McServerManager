using McServerManager.Models.Transactions;

namespace McServerManager.Services.Transactions;

/// <summary>
/// Production fail-closed sentinel. A native handle-relative writer, durable journal, discovery-safe stage location,
/// and runtime start/stop exclusion lease have not been implemented or verified. No System.IO fallback is permitted.
/// </summary>
public sealed class UnsupportedTransactionStorage : ITransactionStorage
{
    public TransactionStorageCapabilities Capabilities { get; } = new(TransactionStorageMode.Unsupported, false, false, false, false);
    public Task<TransactionObservation> ObserveAsync(TransactionReceipt receipt, CancellationToken cancellationToken) =>
        throw new PlatformNotSupportedException("Production transaction storage is disabled.");
    public Task CreateStageAsync(TransactionReceipt receipt, TransactionPlan plan, CancellationToken cancellationToken) =>
        throw new PlatformNotSupportedException("Production transaction storage is disabled.");
    public Task StageArtifactAsync(TransactionReceipt receipt, TransactionArtifact artifact, ReadOnlyMemory<byte> bytes, CancellationToken cancellationToken) =>
        throw new PlatformNotSupportedException("Production transaction storage is disabled.");
    public Task<TransactionPublishDecision> PublishAsync(TransactionReceipt receipt, TransactionPlan plan, CancellationToken cancellationToken) =>
        throw new PlatformNotSupportedException("Production transaction storage is disabled.");
    public Task<TransactionCleanupDecision> CleanupOwnedStageAsync(TransactionReceipt receipt, CancellationToken cancellationToken) =>
        throw new PlatformNotSupportedException("Production transaction storage is disabled.");
}
