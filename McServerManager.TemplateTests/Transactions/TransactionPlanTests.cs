using System.Collections.Immutable;
using McServerManager.Models.Transactions;
using McServerManager.Services.Transactions;

namespace McServerManager.TemplateTests.Transactions;

public sealed class TransactionPlanTests
{
    [Fact]
    public void FingerprintIsDeterministicAndSnapshotsMutableInputs()
    {
        var one = TransactionFixtures.Artifact("one", [1, 2, 3]);
        var two = TransactionFixtures.Artifact("two", [4, 5, 6]);
        var list = new List<TransactionArtifact> { two, one };
        var plan = TransactionFixtures.Plans.Prepare(Guid.NewGuid(), TransactionFixtures.Target, TransactionFixtures.Review, list);
        var hash = TransactionFixtures.Plans.GetFingerprint(plan);
        list[0] = two with { SizeBytes = 900 };
        list.Clear();
        Assert.Equal(2, plan.Artifacts.Length);
        Assert.Equal(hash, TransactionFixtures.Plans.GetFingerprint(plan));
        Assert.Equal(hash, TransactionFixtures.Plans.GetFingerprint(plan with
        {
            Artifacts = plan.Artifacts.Reverse().ToImmutableArray(),
            Review = plan.Review with { Settings = plan.Review.Settings.Reverse().ToImmutableArray() }
        }));
        Assert.Equal(64, hash.Length);
    }

    [Fact]
    public void EveryApprovalRelevantFieldChangesFingerprint()
    {
        var plan = TransactionFixtures.Plan();
        var fingerprint = TransactionFixtures.Plans.GetFingerprint(plan);
        var changed = new[]
        {
            plan with { InstallId = Guid.NewGuid() },
            plan with { Target = plan.Target with { StorageId = "other" } },
            plan with { Target = plan.Target with { AnchorIdentity = "other" } },
            plan with { Target = plan.Target with { TargetName = "other" } },
            plan with { Target = plan.Target with { ServerIdentity = "other" } },
            plan with { Target = plan.Target with { AbsentBaseline = "other" } },
            plan with { Target = plan.Target with { StoppedLease = "other" } },
            plan with { Review = plan.Review with { SourceManifestSha256 = new string('c', 64) } },
            plan with { Review = plan.Review with { InspectionEvidenceSha256 = new string('c', 64) } },
            plan with { Review = plan.Review with { RuntimeVersion = "v2" } },
            plan with { Review = plan.Review with { MemoryMiB = 2048 } },
            plan with { Review = plan.Review with { Port = 25566 } },
            plan with { Review = plan.Review with { Settings = [new("motd", "changed")] } },
            plan with { Artifacts = [plan.Artifacts[0] with { ProjectId = "other" }] },
            plan with { Artifacts = [plan.Artifacts[0] with { VersionId = "other" }] },
            plan with { Artifacts = [plan.Artifacts[0] with { ArtifactId = "other" }] },
            plan with { Artifacts = [plan.Artifacts[0] with { Destination = "other.bin" }] },
            plan with { Artifacts = [plan.Artifacts[0] with { SizeBytes = 999 }] },
            plan with { Artifacts = [plan.Artifacts[0] with { Sha256 = new string('c', 64) }] }
        };
        foreach (var variant in changed) Assert.NotEqual(fingerprint, TransactionFixtures.Plans.GetFingerprint(variant));
    }

    [Theory]
    [InlineData("../escape")]
    [InlineData("/root/file")]
    [InlineData("C:/file")]
    [InlineData("C:\\file")]
    [InlineData("a\\b")]
    [InlineData("file:stream")]
    [InlineData("a//b")]
    [InlineData("a/./b")]
    [InlineData("a/../b")]
    [InlineData("a.")]
    [InlineData("a ")]
    [InlineData("CON.jar")]
    [InlineData("com1.bin")]
    [InlineData("LPT9")]
    [InlineData("NUL.txt")]
    [InlineData("longna~1.bin")]
    [InlineData("a/Config.Json")]
    [InlineData("é.bin")]
    [InlineData("a/CONIN$.bin")]
    [InlineData("a\u0000b")]
    public void RejectsWindowsAliasesTraversalAndDiscoverableConfigOnEveryHost(string path)
    {
        var plan = TransactionFixtures.Plan();
        Assert.Throws<ArgumentException>(() => TransactionFixtures.Plans.GetFingerprint(plan with
        { Artifacts = [plan.Artifacts[0] with { Destination = path }] }));
    }

    [Fact]
    public void RejectsCaseAndFileDirectoryCollisions()
    {
        var plan = TransactionFixtures.Plan();
        var artifact = plan.Artifacts[0];
        foreach (var paths in new[] { new[] { "A.bin", "a.bin" }, new[] { "Dir", "dir/file.bin" } })
            Assert.Throws<ArgumentException>(() => TransactionFixtures.Plans.GetFingerprint(plan with
            { Artifacts = [artifact with { Destination = paths[0] }, artifact with { ArtifactId = "two", Destination = paths[1] }] }));
    }

    [Fact]
    public void EnforcesNumericSourceAndCollectionBounds()
    {
        var plan = TransactionFixtures.Plan();
        foreach (var size in new[] { long.MinValue, -1, 0, TransactionPolicy.MaxArtifactBytes + 1L, long.MaxValue })
            Assert.Throws<ArgumentException>(() => TransactionFixtures.Plans.GetFingerprint(plan with { Artifacts = [plan.Artifacts[0] with { SizeBytes = size }] }));
        foreach (var invalid in new[]
        {
            plan with { InstallId = Guid.Empty },
            plan with { Artifacts = default },
            plan with { Artifacts = [] },
            plan with { Artifacts = [plan.Artifacts[0] with { Provider = "modrinth" }] },
            plan with { Artifacts = [plan.Artifacts[0] with { Sha256 = new string('A', 64) }] },
            plan with { Review = plan.Review with { RuntimeIdentity = "fabric" } },
            plan with { Review = plan.Review with { MemoryMiB = int.MaxValue } },
            plan with { Review = plan.Review with { Port = 0 } },
            plan with { Review = plan.Review with { Settings = [new("same", "1"), new("SAME", "2")] } },
            plan with { Target = plan.Target with { TargetName = "one/two" } }
        }) Assert.Throws<ArgumentException>(() => TransactionFixtures.Plans.GetFingerprint(invalid));
        var tooMany = Enumerable.Range(0, TransactionPolicy.MaxArtifacts + 1).Select(i => plan.Artifacts[0] with { ArtifactId = "id-" + i, Destination = "file-" + i }).ToImmutableArray();
        Assert.Throws<ArgumentException>(() => TransactionFixtures.Plans.GetFingerprint(plan with { Artifacts = tooMany }));
        var tooLarge = tooMany.Take(17).Select(x => x with { SizeBytes = TransactionPolicy.MaxArtifactBytes }).ToImmutableArray();
        Assert.Throws<ArgumentException>(() => TransactionFixtures.Plans.GetFingerprint(plan with { Artifacts = tooLarge }));
        var boundary = tooLarge.Take(16).ToImmutableArray();
        Assert.NotEmpty(TransactionFixtures.Plans.GetFingerprint(plan with { Artifacts = boundary }));
    }

    [Fact]
    public async Task ProductionStorageAndLiveSourcesFailClosedBeforeIO()
    {
        var plan = TransactionFixtures.Plan();
        var source = new SyntheticSource();
        var executor = new TransactionExecutor(TransactionFixtures.Plans, new UnsupportedTransactionStorage(), source);
        var result = await executor.ApplyAsync(plan, TransactionFixtures.Approval(plan));
        Assert.Equal(TransactionStatus.Rejected, result.Status);
        Assert.Equal("StorageOrSourceUnsupported", result.Code);
        Assert.Equal(0, source.Reads);
        await Assert.ThrowsAsync<PlatformNotSupportedException>(() => new UnsupportedTransactionStorage().ObserveAsync(
            new(plan.InstallId, Guid.NewGuid(), plan.Target.StorageId, TransactionFixtures.Approval(plan).PlanFingerprint), default));
    }
}
