using System.Text;
using McServerManager.Models.Transactions;
using McServerManager.Services.Transactions;

namespace McServerManager.TemplateTests.Transactions;

public sealed class TransactionExecutorTests
{
    [LinuxTransactionFact]
    public async Task StagesCompleteVerifiedDirectoryThenPublishesOnceAndReplaysByInstallId()
    {
        using var store = new IsolatedDirectoryTransactionStore();
        var (plan, source, executor) = TransactionFixtures.Setup(store);
        store.AfterStage = path =>
        {
            Assert.False(Directory.Exists(store.TargetPath(plan)));
            Assert.StartsWith(store.StageRoot + Path.DirectorySeparatorChar, path);
            Assert.DoesNotContain(store.TargetsRoot, path, StringComparison.Ordinal);
        };
        var approval = TransactionFixtures.Approval(plan);
        var result = await executor.ApplyAsync(plan, approval);
        Assert.Equal(TransactionStatus.Committed, result.Status);
        Assert.Null(result.RecoveryReceipt);
        Assert.False(Directory.Exists(store.LatestStage));
        Assert.Equal(2, Directory.GetFiles(store.TargetPath(plan), "*", SearchOption.AllDirectories).Length);
        foreach (var artifact in plan.Artifacts)
            Assert.Equal(source.Data[artifact.ArtifactId], File.ReadAllBytes(Path.Combine(store.TargetPath(plan), artifact.Destination)));
        Assert.Empty(Directory.GetFiles(store.Root, "config.json", SearchOption.AllDirectories));
        var replay = await executor.ApplyAsync(plan, approval);
        Assert.Equal(TransactionStatus.Committed, replay.Status);
        Assert.Equal("AlreadyCommitted", replay.Code);
        Assert.Equal(1, store.CreateCalls);
        Assert.Equal(1, store.PublishCalls);
        Assert.Equal(2, source.Reads);
    }

    [LinuxTransactionFact]
    public async Task ChangedApprovalPlanAndReusedInstallIdNeverTouchStorage()
    {
        using var store = new IsolatedDirectoryTransactionStore();
        var (plan, source, executor) = TransactionFixtures.Setup(store);
        var approval = TransactionFixtures.Approval(plan);
        var changed = plan with { Target = plan.Target with { TargetName = "other-server" } };
        var result = await executor.ApplyAsync(changed, approval);
        Assert.Equal("ApprovalMismatch", result.Code);
        Assert.Equal(0, store.CreateCalls);
        Assert.Equal(0, source.Reads);
        Assert.Equal(TransactionStatus.Committed, (await executor.ApplyAsync(plan, approval)).Status);
        var replayDifferentPlan = await executor.ApplyAsync(changed, TransactionFixtures.Approval(changed));
        Assert.Equal(TransactionStatus.Rejected, replayDifferentPlan.Status);
        Assert.False(Directory.Exists(store.TargetPath(changed)));
        Assert.Equal(1, store.CreateCalls);
    }

    [LinuxTransactionTheory]
    [InlineData("hash")]
    [InlineData("short")]
    [InlineData("long")]
    public async Task SourceMutationAfterApprovalRollsBackOwnedStage(string fault)
    {
        using var store = new IsolatedDirectoryTransactionStore();
        var (plan, source, executor) = TransactionFixtures.Setup(store);
        var bytes = source.Data[plan.Artifacts[1].ArtifactId];
        source.Data[plan.Artifacts[1].ArtifactId] = fault switch
        {
            "short" => bytes[..^1],
            "long" => [.. bytes, 1],
            _ => Enumerable.Repeat((byte)'x', bytes.Length).ToArray()
        };
        var result = await executor.ApplyAsync(plan, TransactionFixtures.Approval(plan));
        Assert.Equal(TransactionStatus.Failed, result.Status);
        Assert.Null(result.RecoveryReceipt);
        Assert.False(Directory.Exists(store.TargetPath(plan)));
        Assert.Empty(Directory.GetFileSystemEntries(store.StageRoot));
        Assert.Equal(1, store.CleanupCalls);
        Assert.Equal(0, store.PublishCalls);
    }

    [LinuxTransactionFact]
    public async Task NonseekablePartialReadsAreBoundedAndCanSucceed()
    {
        using var store = new IsolatedDirectoryTransactionStore();
        var (plan, source, executor) = TransactionFixtures.Setup(store);
        source.Open = artifact => new PartialStream(source.Data[artifact.ArtifactId]);
        Assert.Equal(TransactionStatus.Committed, (await executor.ApplyAsync(plan, TransactionFixtures.Approval(plan))).Status);
    }

    [LinuxTransactionFact]
    public async Task OversizedEndlessStreamReadsOnlyApprovedBytesPlusOne()
    {
        using var store = new IsolatedDirectoryTransactionStore();
        var (plan, source, executor) = TransactionFixtures.Setup(store, 1);
        var stream = new EndlessStream();
        source.Open = _ => stream;
        var result = await executor.ApplyAsync(plan, TransactionFixtures.Approval(plan));
        Assert.Equal(TransactionStatus.Failed, result.Status);
        Assert.Equal(plan.Artifacts[0].SizeBytes + 1, stream.BytesRead);
        Assert.Empty(Directory.GetFileSystemEntries(store.StageRoot));
    }

    [LinuxTransactionTheory]
    [InlineData("running")]
    [InlineData("lease")]
    [InlineData("baseline")]
    [InlineData("target")]
    [InlineData("anchor")]
    public async Task FinalPublicationRevalidatesStoppedLeaseTargetIdentityAndBaseline(string change)
    {
        using var store = new IsolatedDirectoryTransactionStore();
        var (plan, _, executor) = TransactionFixtures.Setup(store);
        var foreignTarget = Path.Combine(store.TargetPath(plan), "foreign.txt");
        store.BeforePublish = () =>
        {
            switch (change)
            {
                case "running": store.SetRunning(); break;
                case "lease": store.ReplaceLease(); break;
                case "baseline": store.ChangeBaseline(); break;
                case "anchor":
                    Directory.Move(store.TargetsRoot, Path.Combine(store.Root, "original-targets"));
                    Directory.CreateDirectory(store.TargetsRoot);
                    break;
                case "target":
                    Directory.CreateDirectory(store.TargetPath(plan));
                    File.WriteAllText(foreignTarget, "foreign synthetic bytes");
                    break;
            }
        };
        var result = await executor.ApplyAsync(plan, TransactionFixtures.Approval(plan));
        if (change == "anchor")
        {
            Assert.Equal(TransactionStatus.RecoveryRequired, result.Status);
            Assert.True(Directory.Exists(store.LatestStage));
        }
        else
        {
            Assert.Equal(TransactionStatus.Failed, result.Status);
            Assert.Empty(Directory.GetFileSystemEntries(store.StageRoot));
        }
        if (change == "target") Assert.Equal("foreign synthetic bytes", File.ReadAllText(foreignTarget));
        else Assert.False(Directory.Exists(store.TargetPath(plan)));
    }

    [LinuxTransactionFact]
    public async Task AlreadyRunningOrExistingTargetFailsBeforeCreatingStage()
    {
        using var store = new IsolatedDirectoryTransactionStore();
        var (plan, source, executor) = TransactionFixtures.Setup(store);
        store.SetRunning();
        var result = await executor.ApplyAsync(plan, TransactionFixtures.Approval(plan));
        Assert.Equal(TransactionStatus.Failed, result.Status);
        Assert.Equal(0, source.Reads);
        Assert.Empty(Directory.GetFileSystemEntries(store.StageRoot));
    }

    [LinuxTransactionTheory]
    [InlineData("before")]
    [InlineData("created")]
    [InlineData("staged")]
    [InlineData("publish")]
    public async Task CancellationBeforePublicationCleansOnlyOwnedStage(string point)
    {
        using var store = new IsolatedDirectoryTransactionStore();
        using var cancellation = new CancellationTokenSource();
        var (plan, _, executor) = TransactionFixtures.Setup(store);
        switch (point)
        {
            case "before": cancellation.Cancel(); break;
            case "created": store.AfterCreate = cancellation.Cancel; break;
            case "staged": store.AfterStage = _ => cancellation.Cancel(); break;
            case "publish": store.BeforePublish = cancellation.Cancel; break;
        }
        var result = await executor.ApplyAsync(plan, TransactionFixtures.Approval(plan), cancellation.Token);
        Assert.Equal(TransactionStatus.Cancelled, result.Status);
        Assert.Null(result.RecoveryReceipt);
        Assert.False(Directory.Exists(store.TargetPath(plan)));
        Assert.Empty(Directory.GetFileSystemEntries(store.StageRoot));
    }

    [LinuxTransactionTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CancellationOrExceptionAfterRenameReportsCommittedTruth(bool throws)
    {
        using var store = new IsolatedDirectoryTransactionStore();
        using var cancellation = new CancellationTokenSource();
        var (plan, _, executor) = TransactionFixtures.Setup(store);
        store.AfterPublish = () => { cancellation.Cancel(); if (throws) throw new OperationCanceledException(cancellation.Token); };
        var result = await executor.ApplyAsync(plan, TransactionFixtures.Approval(plan), cancellation.Token);
        Assert.Equal(TransactionStatus.Committed, result.Status);
        Assert.True(Directory.Exists(store.TargetPath(plan)));
        Assert.Equal(0, store.CleanupCalls);
    }

    [LinuxTransactionFact]
    public async Task AmbiguousPublishNeverDeletesAndLaterReconcilesCommitted()
    {
        using var store = new IsolatedDirectoryTransactionStore();
        var (plan, _, executor) = TransactionFixtures.Setup(store);
        store.AfterPublish = () => { store.ObservationUnknown = true; throw new IOException("Injected lost publication response."); };
        var result = await executor.ApplyAsync(plan, TransactionFixtures.Approval(plan));
        Assert.Equal(TransactionStatus.PublicationUnknown, result.Status);
        Assert.NotNull(result.RecoveryReceipt);
        Assert.True(Directory.Exists(store.TargetPath(plan)));
        Assert.Equal(0, store.CleanupCalls);
        store.ObservationUnknown = false;
        Assert.Equal(TransactionStatus.Committed, (await executor.RecoverAsync(result.RecoveryReceipt)).Status);
        Assert.Equal(0, store.CleanupCalls);
    }

    [LinuxTransactionTheory]
    [InlineData("create")]
    [InlineData("write")]
    [InlineData("stage")]
    public async Task FaultsIncludingPartialCreationAndPartialWriteCleanOwnedStage(string fault)
    {
        using var store = new IsolatedDirectoryTransactionStore();
        var (plan, _, executor) = TransactionFixtures.Setup(store);
        if (fault == "create") store.AfterCreate = () => throw new IOException("Injected creation fault after allocation.");
        if (fault == "write") store.FailDuringWrite = true;
        if (fault == "stage") store.AfterStage = _ => throw new IOException("Injected staged fault.");
        var result = await executor.ApplyAsync(plan, TransactionFixtures.Approval(plan));
        Assert.Equal(TransactionStatus.Failed, result.Status);
        Assert.Null(result.RecoveryReceipt);
        Assert.Empty(Directory.GetFileSystemEntries(store.StageRoot));
        Assert.False(Directory.Exists(store.TargetPath(plan)));
    }

    [LinuxTransactionTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CleanupFailureReportsRecoverableReceiptAndRecoveryIsIdempotent(bool throws)
    {
        using var store = new IsolatedDirectoryTransactionStore();
        var (plan, _, executor) = TransactionFixtures.Setup(store);
        store.AfterStage = _ => throw new IOException("Injected failure.");
        store.CleanupFails = !throws;
        store.ThrowCleanup = throws;
        var result = await executor.ApplyAsync(plan, TransactionFixtures.Approval(plan));
        Assert.Equal(TransactionStatus.RecoveryRequired, result.Status);
        Assert.NotNull(result.RecoveryReceipt);
        Assert.True(Directory.Exists(store.LatestStage));
        var forged = result.RecoveryReceipt with { OwnerToken = Guid.NewGuid() };
        store.CleanupFails = false;
        store.ThrowCleanup = false;
        var calls = store.CleanupCalls;
        Assert.Equal(TransactionStatus.Rejected, (await executor.RecoverAsync(forged)).Status);
        Assert.Equal(calls, store.CleanupCalls);
        Assert.Equal(TransactionStatus.Recovered, (await executor.RecoverAsync(result.RecoveryReceipt)).Status);
        Assert.Equal(TransactionStatus.Recovered, (await executor.RecoverAsync(result.RecoveryReceipt)).Status);
        Assert.False(Directory.Exists(store.LatestStage));
        Assert.False(Directory.Exists(store.TargetPath(plan)));
    }

    [LinuxTransactionTheory]
    [InlineData("changed")]
    [InlineData("foreign")]
    [InlineData("symlink")]
    [InlineData("directory-symlink")]
    [InlineData("hardlink")]
    [InlineData("replaced")]
    public async Task ChangedForeignLinkedOrReplacedStagingIsNeverPublishedOrDeleted(string change)
    {
        using var store = new IsolatedDirectoryTransactionStore();
        var (plan, _, executor) = TransactionFixtures.Setup(store, 1);
        var foreign = Path.Combine(store.Root, "foreign-synthetic.txt");
        File.WriteAllText(foreign, "foreign synthetic bytes");
        store.BeforePublish = () =>
        {
            var path = Path.Combine(store.LatestStage!, plan.Artifacts[0].Destination);
            switch (change)
            {
                case "changed": File.WriteAllText(path, "changed bytes"); break;
                case "foreign": File.WriteAllText(Path.Combine(store.LatestStage!, "foreign.bin"), "foreign bytes"); break;
                case "symlink": File.Delete(path); File.CreateSymbolicLink(path, foreign); break;
                case "hardlink": LinuxIdentity.CreateHardLink(path, Path.Combine(store.Root, "foreign-hardlink.bin")); break;
                case "directory-symlink":
                    Directory.Move(store.LatestStage!, Path.Combine(store.Root, "original-stage"));
                    Directory.CreateSymbolicLink(store.LatestStage!, Path.Combine(store.Root, "original-stage"));
                    break;
                case "replaced":
                    // Clone every marker-equivalent byte, but the stage identity is still different.
                    Directory.Move(store.LatestStage!, Path.Combine(store.Root, "original-stage"));
                    Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                    File.Copy(Path.Combine(store.Root, "original-stage", plan.Artifacts[0].Destination), path);
                    break;
            }
        };
        var result = await executor.ApplyAsync(plan, TransactionFixtures.Approval(plan));
        Assert.Equal(TransactionStatus.RecoveryRequired, result.Status);
        Assert.Equal("RefusedForeignOrChanged", result.Code);
        Assert.NotNull(result.RecoveryReceipt);
        Assert.True(Directory.Exists(store.LatestStage));
        Assert.False(Directory.Exists(store.TargetPath(plan)));
        Assert.Equal("foreign synthetic bytes", File.ReadAllText(foreign));
        Assert.Equal(TransactionStatus.RecoveryRequired, (await executor.RecoverAsync(result.RecoveryReceipt)).Status);
    }

    [LinuxTransactionFact]
    public async Task TargetSymlinkAndAtomicNoReplaceNeverOverwriteForeignEntry()
    {
        using var store = new IsolatedDirectoryTransactionStore();
        var (plan, _, executor) = TransactionFixtures.Setup(store);
        store.BeforePublish = () => File.CreateSymbolicLink(store.TargetPath(plan), Path.Combine(store.Root, "missing-foreign-target"));
        var result = await executor.ApplyAsync(plan, TransactionFixtures.Approval(plan));
        Assert.NotEqual(TransactionStatus.Committed, result.Status);
        Assert.NotNull(new FileInfo(store.TargetPath(plan)).LinkTarget);
        Assert.Empty(Directory.GetFileSystemEntries(store.StageRoot));
    }

    [LinuxTransactionFact]
    public async Task MissingCapabilityOrLiveSourceRefusesBeforeStageAndRead()
    {
        using var store = new IsolatedDirectoryTransactionStore();
        var (plan, source, executor) = TransactionFixtures.Setup(store);
        store.Capabilities = store.Capabilities with { ExclusiveStoppedLease = false };
        Assert.Equal(TransactionStatus.Rejected, (await executor.ApplyAsync(plan, TransactionFixtures.Approval(plan))).Status);
        store.Capabilities = store.Capabilities with { ExclusiveStoppedLease = true };
        source.IsSynthetic = false;
        Assert.Equal(TransactionStatus.Rejected, (await executor.ApplyAsync(plan, TransactionFixtures.Approval(plan))).Status);
        Assert.Equal(0, store.CreateCalls);
        Assert.Equal(0, source.Reads);
    }

    [LinuxTransactionFact]
    public async Task ConcurrentSameInstallIdCannotCreateAnotherStageOrCleanupActiveOwner()
    {
        using var store = new IsolatedDirectoryTransactionStore();
        var (plan, source, executor) = TransactionFixtures.Setup(store, 1);
        var opened = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        source.Open = artifact => new PausingStream(source.Data[artifact.ArtifactId], opened, release);
        var first = executor.ApplyAsync(plan, TransactionFixtures.Approval(plan));
        await opened.Task;
        var second = await executor.ApplyAsync(plan, TransactionFixtures.Approval(plan));
        Assert.Equal(TransactionStatus.Rejected, second.Status);
        Assert.Equal(1, store.CreateCalls);
        Assert.Equal(0, store.CleanupCalls);
        release.SetResult();
        Assert.Equal(TransactionStatus.Committed, (await first).Status);
    }

    [LinuxTransactionFact]
    public async Task FailureBetweenAllocationAndIdentityCaptureRemainsUnknownWithoutCleanup()
    {
        using var store = new IsolatedDirectoryTransactionStore();
        var (plan, _, executor) = TransactionFixtures.Setup(store);
        store.AfterDirectoryAllocated = () => throw new IOException("Injected identity-capture failure after mkdir.");
        var result = await executor.ApplyAsync(plan, TransactionFixtures.Approval(plan));
        Assert.Equal(TransactionStatus.PublicationUnknown, result.Status);
        Assert.NotNull(result.RecoveryReceipt);
        Assert.True(Directory.Exists(store.LatestStage));
        Assert.Equal(0, store.CleanupCalls);
        Assert.Equal(TransactionStatus.PublicationUnknown, (await executor.RecoverAsync(result.RecoveryReceipt)).Status);
        Assert.Equal(0, store.CleanupCalls);
    }

    [LinuxTransactionFact]
    public async Task DisappearingStageCannotBeReportedAsRolledBack()
    {
        using var store = new IsolatedDirectoryTransactionStore();
        var (plan, _, executor) = TransactionFixtures.Setup(store);
        var missing = Path.Combine(store.Root, "moved-synthetic-stage");
        store.BeforePublish = () => Directory.Move(store.LatestStage!, missing);
        var result = await executor.ApplyAsync(plan, TransactionFixtures.Approval(plan));
        Assert.Equal(TransactionStatus.PublicationUnknown, result.Status);
        Assert.NotNull(result.RecoveryReceipt);
        Assert.Equal(0, store.CleanupCalls);
        Assert.True(Directory.Exists(missing));
        Assert.Equal(TransactionStatus.PublicationUnknown, (await executor.RecoverAsync(result.RecoveryReceipt)).Status);
        // Restore the exact original inode, not a replacement with the same name/marker.
        Directory.Move(missing, store.LatestStage!);
        Assert.Equal(TransactionStatus.Recovered, (await executor.RecoverAsync(result.RecoveryReceipt)).Status);
        Assert.False(Directory.Exists(store.TargetPath(plan)));
    }

    [LinuxTransactionFact]
    public async Task CommittedTargetAndForeignStageCoexistenceNeverAuthorizesDeletingEither()
    {
        using var store = new IsolatedDirectoryTransactionStore();
        var (plan, _, executor) = TransactionFixtures.Setup(store);
        store.AfterPublish = () =>
        {
            Directory.CreateDirectory(store.LatestStage!);
            File.WriteAllText(Path.Combine(store.LatestStage!, "foreign.txt"), "foreign stage after commit");
            store.ObservationUnknown = true;
            throw new IOException("Injected uncertain outcome.");
        };
        var result = await executor.ApplyAsync(plan, TransactionFixtures.Approval(plan));
        Assert.Equal(TransactionStatus.PublicationUnknown, result.Status);
        Assert.NotNull(result.RecoveryReceipt);
        store.ObservationUnknown = false;
        Assert.Equal(TransactionStatus.Committed, (await executor.RecoverAsync(result.RecoveryReceipt)).Status);
        Assert.Equal("foreign stage after commit", File.ReadAllText(Path.Combine(store.LatestStage!, "foreign.txt")));
        Assert.True(Directory.Exists(store.TargetPath(plan)));
        Assert.Equal(0, store.CleanupCalls);
    }

    [LinuxTransactionFact]
    public async Task CommittedReplayRemainsCommittedEvenWhenRetryWasCancelled()
    {
        using var store = new IsolatedDirectoryTransactionStore();
        var (plan, _, executor) = TransactionFixtures.Setup(store);
        Assert.Equal(TransactionStatus.Committed, (await executor.ApplyAsync(plan, TransactionFixtures.Approval(plan))).Status);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        Assert.Equal(TransactionStatus.Committed, (await executor.ApplyAsync(plan, TransactionFixtures.Approval(plan), cancellation.Token)).Status);
        Assert.Equal(1, store.CreateCalls);
    }

    [LinuxTransactionFact]
    public async Task PostPublicationFinalizationExceptionStillReportsCommitted()
    {
        using var store = new IsolatedDirectoryTransactionStore();
        var (plan, _, executor) = TransactionFixtures.Setup(store);
        store.AfterPublish = () => throw new IOException("Injected post-publication lease finalization failure.");
        var result = await executor.ApplyAsync(plan, TransactionFixtures.Approval(plan));
        Assert.Equal(TransactionStatus.Committed, result.Status);
        Assert.Equal("CommittedAfterReconciliation", result.Code);
        Assert.Equal(0, store.CleanupCalls);
        Assert.True(Directory.Exists(store.TargetPath(plan)));
    }

    [LinuxTransactionFact]
    public async Task UnknownBackendObservationFailsClosedWithoutCleanup()
    {
        using var store = new IsolatedDirectoryTransactionStore();
        var (plan, source, executor) = TransactionFixtures.Setup(store);
        store.ObservationOverride = (TransactionObservation)999;
        var result = await executor.ApplyAsync(plan, TransactionFixtures.Approval(plan));
        Assert.Equal(TransactionStatus.PublicationUnknown, result.Status);
        Assert.Equal(0, store.CreateCalls);
        Assert.Equal(0, store.CleanupCalls);
        Assert.Equal(0, source.Reads);
    }

    [LinuxTransactionFact]
    public async Task ExistingTargetIsNeverOverwrittenEvenBeforeFirstStage()
    {
        using var store = new IsolatedDirectoryTransactionStore();
        var (plan, source, executor) = TransactionFixtures.Setup(store);
        Directory.CreateDirectory(store.TargetPath(plan));
        var foreign = Path.Combine(store.TargetPath(plan), "foreign.txt");
        File.WriteAllText(foreign, "pre-existing synthetic data");
        var result = await executor.ApplyAsync(plan, TransactionFixtures.Approval(plan));
        Assert.Equal(TransactionStatus.Failed, result.Status);
        Assert.Equal("pre-existing synthetic data", File.ReadAllText(foreign));
        Assert.Empty(Directory.GetFileSystemEntries(store.StageRoot));
        Assert.Equal(0, source.Reads);
        Assert.Equal(0, store.CleanupCalls);
    }

    private class PartialStream(byte[] bytes) : MemoryStream(bytes, writable: false)
    {
        public override bool CanSeek => false;
        public override long Length => throw new NotSupportedException();
        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default) =>
            base.ReadAsync(buffer[..Math.Min(buffer.Length, 2)], cancellationToken);
    }
    private sealed class EndlessStream : Stream
    {
        public long BytesRead { get; private set; }
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override int Read(byte[] buffer, int offset, int count) { Array.Fill(buffer, (byte)1, offset, count); BytesRead += count; return count; }
        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        { cancellationToken.ThrowIfCancellationRequested(); buffer.Span.Fill(1); BytesRead += buffer.Length; return ValueTask.FromResult(buffer.Length); }
        public override void Flush() => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
    private sealed class PausingStream(byte[] bytes, TaskCompletionSource opened, TaskCompletionSource release) : PartialStream(bytes)
    {
        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            opened.TrySetResult();
            await release.Task.WaitAsync(cancellationToken);
            return await base.ReadAsync(buffer, cancellationToken);
        }
    }
}
