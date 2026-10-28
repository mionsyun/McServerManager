using System.Security.Cryptography;
using McServerManager.Models.Transactions;

namespace McServerManager.Services.Transactions;

/// <summary>Bounded synthetic core; deliberately not registered in production dependency injection or WPF.</summary>
public sealed class TransactionExecutor(ITransactionPlanService plans, ITransactionStorage storage,
    ISyntheticArtifactSource source) : ITransactionExecutor
{
    public async Task<TransactionResult> ApplyAsync(TransactionPlan plan, TransactionApproval approval,
        CancellationToken cancellationToken = default)
    {
        string fingerprint;
        try { fingerprint = plans.GetFingerprint(plan); }
        catch (ArgumentException ex) { return new(TransactionStatus.Rejected, "InvalidPlan", ex.Message); }
        if (approval is null || !string.Equals(approval.PlanFingerprint, fingerprint, StringComparison.Ordinal))
            return new(TransactionStatus.Rejected, "ApprovalMismatch");
        if (!Supported() || !source.IsSynthetic) return new(TransactionStatus.Rejected, "StorageOrSourceUnsupported");

        var receipt = new TransactionReceipt(plan.InstallId, Guid.NewGuid(), plan.Target.StorageId, fingerprint);
        // A caller never receives another attempt's ownership capability. Replays do not duplicate publication.
        var initial = await ObserveSafelyAsync(receipt).ConfigureAwait(false);
        if (initial == TransactionObservation.Published) return new(TransactionStatus.Committed, "AlreadyCommitted");
        if (initial == TransactionObservation.Unknown) return new(TransactionStatus.PublicationUnknown, "InitialStateUnknown", RecoveryReceipt: receipt);
        if (initial != TransactionObservation.NotStarted) return new(TransactionStatus.Rejected, "InstallIdAlreadyReserved");
        if (cancellationToken.IsCancellationRequested) return new(TransactionStatus.Cancelled, "CancelledBeforeStaging");
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            await storage.CreateStageAsync(receipt, plan, cancellationToken).ConfigureAwait(false);
            foreach (var artifact in plan.Artifacts)
            {
                var bytes = await ReadVerifiedAsync(artifact, cancellationToken).ConfigureAwait(false);
                await storage.StageArtifactAsync(receipt, artifact, bytes, cancellationToken).ConfigureAwait(false);
            }
            cancellationToken.ThrowIfCancellationRequested();
            var decision = await storage.PublishAsync(receipt, plan, cancellationToken).ConfigureAwait(false);
            if (decision == TransactionPublishDecision.Published)
                return new(TransactionStatus.Committed, "Published"); // Cancellation can no longer turn this into rollback.
            return await ReconcileAsync(receipt, TransactionStatus.Rejected, "PublicationRejected", "").ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            var status = ex is OperationCanceledException && cancellationToken.IsCancellationRequested
                ? TransactionStatus.Cancelled : TransactionStatus.Failed;
            return await ReconcileAsync(receipt, status, status == TransactionStatus.Cancelled ? "Cancelled" : "OperationFailed", ex.Message).ConfigureAwait(false);
        }
    }

    public async Task<TransactionResult> RecoverAsync(TransactionReceipt receipt, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(receipt);
        if (!Supported()) return new(TransactionStatus.Rejected, "StorageUnsupported");
        if (receipt.InstallId == Guid.Empty || receipt.OwnerToken == Guid.Empty)
            return new(TransactionStatus.Rejected, "InvalidReceipt");
        try { TransactionPolicy.ValidateIdentity(receipt.StorageId); TransactionPolicy.ValidateSha256(receipt.PlanFingerprint); }
        catch (ArgumentException ex) { return new(TransactionStatus.Rejected, "InvalidReceipt", ex.Message); }
        if (cancellationToken.IsCancellationRequested) return new(TransactionStatus.Cancelled, "RecoveryCancelled", RecoveryReceipt: receipt);
        // Once recovery starts, reconciliation/cleanup are non-cancellable, so cleanup cancellation never hides leftovers.
        return await ReconcileAsync(receipt, TransactionStatus.Recovered, "OwnedStageRecovered", "").ConfigureAwait(false);
    }

    private bool Supported()
    {
        var c = storage.Capabilities;
        return c.Mode == TransactionStorageMode.IsolatedSyntheticTests && c.AtomicNoReplacePublish &&
            c.ExclusiveStoppedLease && c.VerifiedOwnedStage && c.StageOutsideDiscovery;
    }

    private async Task<byte[]> ReadVerifiedAsync(TransactionArtifact artifact, CancellationToken cancellationToken)
    {
        await using var stream = await source.OpenReadAsync(artifact, cancellationToken).ConfigureAwait(false);
        var bytes = new byte[checked((int)artifact.SizeBytes)];
        var offset = 0;
        while (offset < bytes.Length)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var count = await stream.ReadAsync(bytes.AsMemory(offset), cancellationToken).ConfigureAwait(false);
            if (count == 0) throw new InvalidDataException("Synthetic source is shorter than the approved size.");
            offset = checked(offset + count);
        }
        var extra = new byte[1];
        if (await stream.ReadAsync(extra, cancellationToken).ConfigureAwait(false) != 0)
            throw new InvalidDataException("Synthetic source exceeds the approved size.");
        var digest = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
        if (!string.Equals(digest, artifact.Sha256, StringComparison.Ordinal))
            throw new InvalidDataException("Synthetic source hash differs from the approved exact bytes.");
        return bytes;
    }

    private async Task<TransactionObservation> ObserveSafelyAsync(TransactionReceipt receipt)
    {
        try
        {
            var observed = await storage.ObserveAsync(receipt, CancellationToken.None).ConfigureAwait(false);
            return Enum.IsDefined(observed) ? observed : TransactionObservation.Unknown;
        }
        catch (Exception) { return TransactionObservation.Unknown; }
    }

    private async Task<TransactionResult> ReconcileAsync(TransactionReceipt receipt, TransactionStatus failureStatus, string code, string detail)
    {
        var observed = await ObserveSafelyAsync(receipt).ConfigureAwait(false);
        switch (observed)
        {
            case TransactionObservation.Published:
                return new(TransactionStatus.Committed, "CommittedAfterReconciliation", detail);
            case TransactionObservation.NotStarted:
                return new(failureStatus, code, detail);
            case TransactionObservation.OtherAttempt:
            case TransactionObservation.ConflictingPlan:
                return new(TransactionStatus.Rejected, "ReceiptDoesNotOwnAttempt", detail);
            case TransactionObservation.OwnedStage:
                try
                {
                    var cleanup = await storage.CleanupOwnedStageAsync(receipt, CancellationToken.None).ConfigureAwait(false);
                    if (cleanup is TransactionCleanupDecision.Removed or TransactionCleanupDecision.AlreadyAbsent)
                        return new(failureStatus, code, detail);
                    return new(TransactionStatus.RecoveryRequired, cleanup.ToString(), detail, receipt);
                }
                catch (Exception ex) { return new(TransactionStatus.RecoveryRequired, "CleanupFailed", ex.Message, receipt); }
            default:
                return new(TransactionStatus.PublicationUnknown, "PublicationStateUnknown", detail, receipt);
        }
    }
}
