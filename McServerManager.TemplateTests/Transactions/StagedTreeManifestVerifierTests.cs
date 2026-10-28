using System.Collections.Immutable;
using McServerManager.Models.Transactions;
using McServerManager.Services.Transactions;

namespace McServerManager.TemplateTests.Transactions;

public sealed class StagedTreeManifestVerifierTests
{
    private static TransactionPlan Plan => TransactionFixtures.Plan(null,
        TransactionFixtures.Artifact("one", [1, 2, 3], "Payloads/Nested/one.bin"),
        TransactionFixtures.Artifact("two", [4, 5], "Payloads/two.bin"),
        TransactionFixtures.Artifact("three", [6], "root.bin"));

    // An explicit oracle, not a call to the verifier's expected-tree builder.
    private static ImmutableArray<StagedTreeEntry> Snapshot =>
    [
        new("Payloads", StagedTreeEntryKind.Directory, 0, null),
        new("Payloads/Nested", StagedTreeEntryKind.Directory, 0, null),
        new("Payloads/Nested/one.bin", StagedTreeEntryKind.File, 3,
            "039058c6f2c0cb492c533b0a4d14ef77cc0f78abccced5287d84a1a2011cfb81"),
        new("Payloads/two.bin", StagedTreeEntryKind.File, 2,
            "2fa1b377bf67309f65e5e7bc9d924345ca648dec4e601a398a9cb497dcba3765"),
        new("root.bin", StagedTreeEntryKind.File, 1,
            "67586e98fad27da0b9968bc039a1ef34c939b9b8e523a8bef89d478608c5ecf6")
    ];

    [Fact]
    public void ExactWholeNamespaceMatchesRegardlessOfOrder()
    {
        Assert.Equal(StagedTreeManifestComparison.ContentMatches, StagedTreeManifestVerifier.Compare(Plan, Snapshot));
        Assert.Equal(StagedTreeManifestComparison.ContentMatches,
            StagedTreeManifestVerifier.Compare(Plan, Snapshot.Reverse().ToImmutableArray()));
    }

    [Theory]
    [InlineData(0)] [InlineData(1)] [InlineData(2)] [InlineData(3)] [InlineData(4)]
    public void MissingFileOrImplicitParentFails(int index) => Assert.Equal(StagedTreeManifestComparison.MissingEntry,
        StagedTreeManifestVerifier.Compare(Plan, Snapshot.RemoveAt(index)));

    [Fact]
    public void MissingSnapshotDiffersFromEmptyTree()
    {
        Assert.Equal(StagedTreeManifestComparison.InvalidSnapshot, StagedTreeManifestVerifier.Compare(Plan, default));
        Assert.Equal(StagedTreeManifestComparison.MissingEntry, StagedTreeManifestVerifier.Compare(Plan, []));
        Assert.Equal(StagedTreeManifestComparison.InvalidSnapshot,
            StagedTreeManifestVerifier.Compare(Plan, Enumerable.Repeat(Snapshot[0], StagedTreeManifestVerifier.MaxEntries + 1).ToImmutableArray()));
    }

    [Theory]
    [InlineData("extra", true)] [InlineData("Payloads/empty", true)]
    [InlineData("foreign.bin", false)] [InlineData("payloads", true)]
    [InlineData("Payloads/TWO.bin", false)]
    public void ForeignEmptyDirectoriesFilesAndWrongCasingFail(string path, bool directory)
    {
        var extra = directory ? new StagedTreeEntry(path, StagedTreeEntryKind.Directory, 0, null)
            : Snapshot[4] with { RelativePath = path };
        Assert.NotEqual(StagedTreeManifestComparison.ContentMatches, StagedTreeManifestVerifier.Compare(Plan, Snapshot.Add(extra)));
        Assert.NotEqual(StagedTreeManifestComparison.ContentMatches,
            StagedTreeManifestVerifier.Compare(Plan, Snapshot.Insert(0, extra)));
    }

    [Theory]
    [InlineData(0)] [InlineData(1)] [InlineData(2)] [InlineData(3)] [InlineData(4)]
    public void DuplicateFilesAndDirectoriesFail(int index) => Assert.Equal(StagedTreeManifestComparison.DuplicateEntry,
        StagedTreeManifestVerifier.Compare(Plan, Snapshot.Add(Snapshot[index])));

    [Theory]
    [InlineData("../escape")] [InlineData("file:stream")] [InlineData("CON.bin")]
    [InlineData("a\\b")] [InlineData("a//b")] [InlineData("")] [InlineData("/absolute")]
    [InlineData("a/Config.Json")] [InlineData("a/../b")] [InlineData("a.")]
    public void UnsafeSnapshotPathsFail(string path) => Assert.Equal(StagedTreeManifestComparison.InvalidEntry,
        StagedTreeManifestVerifier.Compare(Plan, Snapshot.SetItem(0, Snapshot[0] with { RelativePath = path })));

    [Fact]
    public void NullAndMalformedMetadataFailClosed()
    {
        StagedTreeEntry[] invalid =
        [
            null!, Snapshot[0] with { RelativePath = null! }, Snapshot[0] with { SizeBytes = 1 },
            Snapshot[0] with { Sha256 = "" }, Snapshot[0] with { Sha256 = Snapshot[2].Sha256 },
            Snapshot[0] with { Kind = (StagedTreeEntryKind)42 }, Snapshot[2] with { Sha256 = null },
            Snapshot[2] with { Sha256 = new string('A', 64) }, Snapshot[2] with { Sha256 = new string('z', 64) },
            Snapshot[2] with { Sha256 = "short" }, Snapshot[2] with { SizeBytes = 0 },
            Snapshot[2] with { SizeBytes = -1 }, Snapshot[2] with { SizeBytes = long.MaxValue }
        ];
        foreach (var entry in invalid)
            Assert.Equal(StagedTreeManifestComparison.InvalidEntry, StagedTreeManifestVerifier.Compare(Plan, Snapshot.SetItem(0, entry)));
    }

    [Fact]
    public void WrongHashSizeAndEntryTypeFail()
    {
        foreach (var entry in new[]
        {
            Snapshot[2] with { Sha256 = new string('a', 64) }, Snapshot[2] with { SizeBytes = 2 },
            Snapshot[2] with { Kind = StagedTreeEntryKind.Directory, SizeBytes = 0, Sha256 = null }
        })
            Assert.Equal(StagedTreeManifestComparison.ContentMismatch, StagedTreeManifestVerifier.Compare(Plan, Snapshot.SetItem(2, entry)));
        Assert.Equal(StagedTreeManifestComparison.ContentMismatch, StagedTreeManifestVerifier.Compare(Plan,
            Snapshot.SetItem(0, Snapshot[4] with { RelativePath = "Payloads" })));
    }

    [Fact]
    public void InvalidPlanNeverProducesMatch()
    {
        foreach (var plan in new[] { null!, Plan with { Artifacts = default }, Plan with { Artifacts = [] },
                     Plan with { Target = null! }, Plan with { Artifacts = [null!] } })
            Assert.Equal(StagedTreeManifestComparison.InvalidPlan, StagedTreeManifestVerifier.Compare(plan, Snapshot));
    }

    [Theory]
    [InlineData("Mods/a.bin", "mods/b.bin")]
    [InlineData("a/Mods/a.bin", "a/mods/b.bin")]
    [InlineData("A/mods/a.bin", "a/mods/b.bin")]
    [InlineData("Mods", "mods/b.bin")]
    public void PlanRejectsAmbiguousDirectoryNamespaceInEitherOrder(string first, string second)
    {
        var a = TransactionFixtures.Artifact("one", [1], first);
        var b = TransactionFixtures.Artifact("two", [2], second);
        Assert.Throws<ArgumentException>(() => TransactionFixtures.Plan(null, a, b));
        Assert.Throws<ArgumentException>(() => TransactionFixtures.Plan(null, b, a));
    }

    [Fact]
    public void MaximumNamespaceAtMaximumDepthMatches()
    {
        var artifacts = Enumerable.Range(0, TransactionPolicy.MaxArtifacts).Select(i =>
            TransactionFixtures.Artifact("item-" + i, [1], $"dir-{i}/a/b/c/d/e/f/file.bin")).ToArray();
        var plan = TransactionFixtures.Plan(null, artifacts);
        var entries = ImmutableArray.CreateBuilder<StagedTreeEntry>();
        foreach (var artifact in artifacts)
        {
            string[] parents = ["a", "b", "c", "d", "e", "f"];
            var prefix = artifact.Destination.Split('/')[0];
            entries.Add(new(prefix, StagedTreeEntryKind.Directory, 0, null));
            foreach (var parent in parents)
            {
                prefix += "/" + parent;
                entries.Add(new(prefix, StagedTreeEntryKind.Directory, 0, null));
            }
            entries.Add(new(artifact.Destination, StagedTreeEntryKind.File, artifact.SizeBytes, artifact.Sha256));
        }
        Assert.Equal(StagedTreeManifestVerifier.MaxEntries, entries.Count);
        Assert.Equal(StagedTreeManifestComparison.ContentMatches, StagedTreeManifestVerifier.Compare(plan, entries.ToImmutable()));
    }

    [LinuxTransactionFact]
    public async Task FixturePublishesFreshlyReadNestedAndRootFiles()
    {
        using var store = new IsolatedDirectoryTransactionStore();
        var source = new SyntheticSource();
        source.Data["one"] = [1, 2, 3]; source.Data["two"] = [4, 5]; source.Data["three"] = [6];
        var plan = Plan with { Target = store.Target() };
        var executor = new TransactionExecutor(TransactionFixtures.Plans, store, source);
        Assert.Equal(TransactionStatus.Committed, (await executor.ApplyAsync(plan, TransactionFixtures.Approval(plan))).Status);
        foreach (var artifact in plan.Artifacts)
            Assert.Equal(source.Data[artifact.ArtifactId], File.ReadAllBytes(Path.Combine(store.TargetPath(plan), artifact.Destination)));
        Assert.Equal(2, Directory.GetDirectories(store.TargetPath(plan), "*", SearchOption.AllDirectories).Length);
        Assert.Equal(3, Directory.GetFiles(store.TargetPath(plan), "*", SearchOption.AllDirectories).Length);
    }

    [LinuxTransactionFact]
    public async Task AmbiguousPlanIsRejectedBeforeStorageOrSourceAccess()
    {
        using var store = new IsolatedDirectoryTransactionStore();
        var (plan, source, executor) = TransactionFixtures.Setup(store);
        var malformed = plan with { Artifacts =
        [
            plan.Artifacts[0] with { Destination = "Mods/a.bin" },
            plan.Artifacts[1] with { Destination = "mods/b.bin" }
        ] };
        var result = await executor.ApplyAsync(malformed, TransactionFixtures.Approval(plan));
        Assert.Equal("InvalidPlan", result.Code);
        Assert.Equal(TransactionStatus.Rejected, result.Status);
        Assert.Equal(0, store.CreateCalls);
        Assert.Equal(0, store.PublishCalls);
        Assert.Equal(0, store.CleanupCalls);
        Assert.Equal(0, source.Reads);
        Assert.Empty(Directory.GetFileSystemEntries(store.StageRoot));
        Assert.Empty(Directory.GetFileSystemEntries(store.TargetsRoot));
    }

    [LinuxTransactionTheory]
    [InlineData("hash")] [InlineData("size")] [InlineData("foreign")] [InlineData("missing")]
    public async Task JournalConsistentButUnapprovedStageCannotPublish(string fault)
    {
        using var store = new IsolatedDirectoryTransactionStore();
        var plan = TransactionFixtures.Plan(store.Target(), TransactionFixtures.Artifact("one", [1, 2, 3]));
        var receipt = new TransactionReceipt(plan.InstallId, Guid.NewGuid(), plan.Target.StorageId,
            TransactionFixtures.Plans.GetFingerprint(plan));
        await store.CreateStageAsync(receipt, plan, default);
        // Bypass executor source verification deliberately: the store's journal matches what it wrote,
        // so TreeMatches alone is insufficient. The independent plan comparison must refuse publish.
        if (fault != "missing")
            await store.StageArtifactAsync(receipt, plan.Artifacts[0], fault switch
            {
                "hash" => new byte[] { 3, 2, 1 },
                "size" => new byte[] { 1, 2 },
                _ => new byte[] { 1, 2, 3 }
            }, default);
        if (fault == "foreign")
            await store.StageArtifactAsync(receipt, TransactionFixtures.Artifact("extra", [4], "extra/file.bin"), new byte[] { 4 }, default);
        await Assert.ThrowsAsync<IOException>(() => store.PublishAsync(receipt, plan, default));
        Assert.False(Directory.Exists(store.TargetPath(plan)));
        Assert.Equal(TransactionObservation.OwnedStage, await store.ObserveAsync(receipt, default));
        Assert.Equal(TransactionCleanupDecision.Removed, await store.CleanupOwnedStageAsync(receipt, default));
        Assert.Empty(Directory.GetFileSystemEntries(store.StageRoot));
    }
}
