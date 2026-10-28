using System.Text.Json;
using McServerManager.Models.Dependencies;
using McServerManager.Services.Dependencies;

namespace McServerManager.TemplateTests;

public sealed class DependencyPlannerTests
{
    private readonly DependencyPlanner _planner = new();

    [Fact]
    public void D01_ExactTransitiveDependenciesProduceOneBoundedReviewPlan()
    {
        var c = Evidence("c");
        var b = Evidence("b", Required(c.Artifact));
        var a = Evidence("a", Required(b.Artifact));

        var plan = _planner.Plan(Request([a.Artifact], [c, a, b]));

        Assert.True(plan.IsResolved);
        Assert.Equal(3, plan.Items.Count);
        Assert.All(plan.Items, item => Assert.Equal(DependencyPlanAction.Add, item.Action));
        Assert.Equal(new[] { "a", "b", "c" }, Item(plan, "c").Reasons.Single().Chain.Select(x => x.ProjectId));
        Assert.Contains("not installation authorization", plan.Assurance);
    }

    [Fact]
    public void D02_ExactInstalledArtifactIsKeptWithoutAnotherAddition()
    {
        var c = Evidence("c");
        var a = Evidence("a", Required(c.Artifact));
        var plan = _planner.Plan(Request([a.Artifact], [a, c], [c.Artifact]));

        Assert.True(plan.IsResolved);
        Assert.Equal(DependencyPlanAction.Keep, Item(plan, "c").Action);
        Assert.Single(plan.Items.Where(x => x.Artifact.ProjectId == "c"));
    }

    [Fact]
    public void D03_ChangedInstalledVersionRequiresReviewAndNeverAutoReplaces()
    {
        var old = Evidence("c", version: "1");
        var required = Evidence("c", version: "2");
        var a = Evidence("a", Required(required.Artifact));
        var plan = _planner.Plan(Request([a.Artifact], [a, required, old], [old.Artifact]));

        Blocked(plan, "PinnedVersionConflict");
        var replacement = Assert.Single(plan.Items.Where(x => x.Artifact == required.Artifact));
        Assert.Equal(DependencyPlanAction.ReplacementRequiresReview, replacement.Action);
        Assert.Equal(old.Artifact, replacement.InstalledArtifact);
        Assert.Equal(DependencyPlanAction.Keep, Assert.Single(plan.Items.Where(x => x.Artifact == old.Artifact)).Action);
        Assert.Equal("1", old.Artifact.VersionId);
    }

    [Fact]
    public void D04_DifferentPinnedRequirementsPreserveBothParentChains()
    {
        var c1 = Evidence("c", version: "1");
        var c2 = Evidence("c", version: "2");
        var a = Evidence("a", Required(c1.Artifact));
        var b = Evidence("b", Required(c2.Artifact));
        var plan = _planner.Plan(Request([a.Artifact, b.Artifact], [a, b, c1, c2]));

        Blocked(plan, "PinnedVersionConflict");
        var conflicts = plan.Diagnostics.Where(x => x.Code == "PinnedVersionConflict").ToArray();
        Assert.Contains(conflicts, x => x.Chain.Select(y => y.ProjectId).SequenceEqual(["a", "c"]));
        Assert.Contains(conflicts, x => x.Chain.Select(y => y.ProjectId).SequenceEqual(["b", "c"]));
    }

    [Fact]
    public void D05_OptionalSelectionRecomputesTransitiveDependenciesFromScratch()
    {
        var f = Evidence("f");
        var e = Evidence("e", Required(f.Artifact));
        var a = Evidence("a", Required(e.Artifact) with { Relation = DependencyRelation.Optional });
        var request = Request([a.Artifact], [a, e, f]);
        var unselected = _planner.Plan(request);
        var selected = _planner.Plan(request with { SelectedArtifacts = [a.Artifact, e.Artifact] });
        var deselected = _planner.Plan(request);

        Assert.Single(unselected.Items);
        Assert.False(Assert.Single(unselected.OptionalSuggestions).IsSelected);
        Assert.Equal(3, selected.Items.Count);
        Assert.True(Assert.Single(selected.OptionalSuggestions).IsSelected);
        Assert.Single(deselected.Items);
    }

    [Fact]
    public void D06_SharedDependencyHasOneItemAndBothReasons()
    {
        var c = Evidence("c");
        var a = Evidence("a", Required(c.Artifact));
        var b = Evidence("b", Required(c.Artifact));
        var plan = _planner.Plan(Request([b.Artifact, a.Artifact], [c, b, a]));

        Assert.True(plan.IsResolved);
        Assert.Single(plan.Items.Where(x => x.Artifact.ProjectId == "c"));
        Assert.Equal(new[] { "a", "b" }, Item(plan, "c").Reasons.Select(x => x.Chain.First().ProjectId));
    }

    [Fact]
    public void D07_VerifiedNestedModAndAliasSatisfyDependenciesWithoutExtraArtifact()
    {
        var parent = Evidence("parent");
        parent = parent with { ProvidedMods = [.. parent.ProvidedMods, Provided("bundled", parent.Artifact, ProvidedModKind.Nested), Provided("alias", parent.Artifact, ProvidedModKind.Alias)] };
        var a = Evidence("a", Mod("alias"), Mod("bundled"));
        var plan = _planner.Plan(Request([a.Artifact, parent.Artifact], [a, parent]));

        Assert.True(plan.IsResolved);
        Assert.Equal(2, plan.Items.Count);
    }

    [Fact]
    public void D07_UnverifiedNestedContentIsNotSatisfiedByItsDeclaredPath()
    {
        var parent = Evidence("parent");
        parent = parent with { ProvidedMods = [.. parent.ProvidedMods, Provided("bundled", parent.Artifact, ProvidedModKind.Nested) with { Status = DependencyEvidenceStatus.Unknown }] };
        var a = Evidence("a", Mod("bundled"));
        var plan = _planner.Plan(Request([a.Artifact, parent.Artifact], [a, parent]));

        Blocked(plan, "InvalidProvidedMods");
        Assert.Contains(plan.Diagnostics, x => x.Code == "UnresolvedModId");
    }

    [Fact]
    public void D08_ClientOnlyHardDependencyBlocksDedicatedServerPlan()
    {
        var b = Evidence("b") with { Side = DependencySide.Client };
        var a = Evidence("a", Required(b.Artifact));
        var plan = _planner.Plan(Request([a.Artifact], [a, b]));

        Blocked(plan, "UnsupportedSide");
        Assert.Equal(new[] { "a", "b" }, Assert.Single(plan.Diagnostics.Where(x => x.Code == "UnsupportedSide")).Chain.Select(x => x.ProjectId));
    }

    [Fact]
    public void D09_ModIdCannotBecomeCatalogProjectByMatchingName()
    {
        var b = Evidence("library");
        var a = Evidence("a", Mod("library"));
        var plan = _planner.Plan(Request([a.Artifact], [a, b]));

        Blocked(plan, "UnresolvedModId");
        Assert.Single(plan.Items);
    }

    [Fact]
    public void D09_VerifiedBindingMustMatchActualModIdentityAndDigest()
    {
        var b = Evidence("library");
        var a = Evidence("a", Mod("library"));
        var request = Request([a.Artifact], [a, b]) with { ModBindings = [Binding(b)] };
        Assert.True(_planner.Plan(request).IsResolved);

        var wrongHash = request with { ModBindings = [Binding(b) with { EvidenceContentSha256 = new string('f', 64) }] };
        Blocked(_planner.Plan(wrongHash), "UnresolvedModId");
        var wrongMod = request with { ModBindings = [Binding(b) with { ModId = "not_library" }] };
        Blocked(_planner.Plan(wrongMod), "UnresolvedModId");
    }

    [Fact]
    public void D09_AmbiguousVerifiedBindingsBlockWithoutChoosingFirstResult()
    {
        var b = Evidence("one");
        var c = Evidence("two");
        b = b with { ProvidedMods = [Provided("library", b.Artifact)] };
        c = c with { ProvidedMods = [Provided("library", c.Artifact)] };
        var a = Evidence("a", Mod("library"));
        var plan = _planner.Plan(Request([a.Artifact], [a, b, c]) with { ModBindings = [Binding(b), Binding(c)] });

        Blocked(plan, "AmbiguousModId");
        Assert.Single(plan.Items);
    }

    [Fact]
    public void D10_MetadataOnlyRequiredDependencyIsNotErasedByApi()
    {
        var b = Evidence("b");
        var a = Evidence("a", Mod("b"));
        var plan = _planner.Plan(Request([a.Artifact], [a, b]) with { ModBindings = [Binding(b)] });

        Assert.True(plan.IsResolved);
        Assert.Equal(2, plan.Items.Count);
        Assert.Equal(DependencyEvidenceSource.FabricMetadata, Item(plan, "b").Reasons.Single().Source);
    }

    [Fact]
    public void D11_FabricConflictsRetainsWarningSemantics()
    {
        var b = Evidence("b");
        var a = Evidence("a", Mod("b") with { Relation = DependencyRelation.Warning, OriginalRelation = "conflicts" });
        var plan = _planner.Plan(Request([a.Artifact, b.Artifact], [a, b]));

        Assert.True(plan.IsResolved);
        Assert.Equal(DependencyDiagnosticSeverity.Warning, Assert.Single(plan.Diagnostics).Severity);
        Assert.Equal("LoaderConflictWarning", plan.Diagnostics.Single().Code);
    }

    [Fact]
    public void D12_CurseForgeRemainsDisabledRegardlessOfRelationLabel()
    {
        var cf = Evidence("cf");
        cf = cf with { Artifact = cf.Artifact with { Provider = DependencyProvider.CurseForge } };
        var plan = _planner.Plan(Request([cf.Artifact], [cf]));

        Blocked(plan, "DisabledProvider");
        Assert.Empty(plan.Items);
    }

    [Fact]
    public void D13_CyclesAreBoundedAndIdentifiedAsMvpPolicy()
    {
        var a = Evidence("a");
        var b = Evidence("b", Required(a.Artifact));
        a = a with { Requirements = [Required(b.Artifact)] };
        var plan = _planner.Plan(Request([a.Artifact], [a, b]));

        Blocked(plan, "UnsupportedCycle");
        var cycle = Assert.Single(plan.Diagnostics.Where(x => x.Code == "UnsupportedCycle"));
        Assert.Equal(new[] { "a", "b", "a" }, cycle.Chain.Select(x => x.ProjectId));
        Assert.Contains("MVP policy", cycle.Message);
    }

    [Fact]
    public void D18_ProviderDistributionDenialFailsClosed()
    {
        var a = Evidence("a") with { DistributionAllowed = false };
        Blocked(_planner.Plan(Request([a.Artifact], [a])), "DistributionUnavailable");
    }

    [Theory]
    [InlineData(DependencyEvidenceStatus.Unknown)]
    [InlineData(DependencyEvidenceStatus.Missing)]
    [InlineData(DependencyEvidenceStatus.Unsupported)]
    [InlineData(DependencyEvidenceStatus.Invalid)]
    public void MissingOrUnsupportedMetadataNeverMeansNoDependencies(DependencyEvidenceStatus status)
    {
        var a = Evidence("a") with { MetadataStatus = status };
        Blocked(_planner.Plan(Request([a.Artifact], [a])), "UnverifiedEvidence");
    }

    [Fact]
    public void ExactPinsMustMatchProviderAndInspectedBytes()
    {
        var a = Evidence("a") with { InspectedContentSha256 = new string('f', 64) };
        Blocked(_planner.Plan(Request([a.Artifact], [a])), "IdentityMismatch");
    }

    [Fact]
    public void UnknownAndRangeRequirementsAreExplicitBlockers()
    {
        var a = Evidence("a", Mod("b") with { ConstraintKind = DependencyConstraintKind.Range, VersionConstraint = ">=2" });
        Blocked(_planner.Plan(Request([a.Artifact], [a])), "UnsupportedConstraint");
        a = a with { Requirements = [Mod("b") with { ConstraintKind = DependencyConstraintKind.Unknown }] };
        Blocked(_planner.Plan(Request([a.Artifact], [a])), "UnsupportedConstraint");
    }

    [Fact]
    public void PlatformIdsAreComparedLocallyAndNeverTreatedAsCatalogMods()
    {
        var a = Evidence("a", Mod("minecraft", "1.21.1"), Mod("fabricloader", "0.16.0"), Mod("java", "21"));
        Assert.True(_planner.Plan(Request([a.Artifact], [a])).IsResolved);
        a = a with { Requirements = [Mod("java", "17")] };
        Blocked(_planner.Plan(Request([a.Artifact], [a])), "PlatformVersionMismatch");
    }

    [Fact]
    public void HardIncompatibilityBlocksWhileRangeWarningStaysWarning()
    {
        var b = Evidence("b");
        var a = Evidence("a", Mod("b") with { Relation = DependencyRelation.Incompatible });
        Blocked(_planner.Plan(Request([a.Artifact, b.Artifact], [a, b])), "IncompatibleDependency");
        a = a with { Requirements = [Mod("b") with { Relation = DependencyRelation.Warning, ConstraintKind = DependencyConstraintKind.Range, VersionConstraint = ">=1" }] };
        var warningPlan = _planner.Plan(Request([a.Artifact, b.Artifact], [a, b]));
        Assert.True(warningPlan.IsResolved);
        Assert.Single(warningPlan.Diagnostics);
    }

    [Fact]
    public void ReversedInputOrderProducesByteIdenticalPlan()
    {
        var c = Evidence("c");
        var b = Evidence("b", Required(c.Artifact));
        var a = Evidence("a", Required(b.Artifact), Required(c.Artifact));
        var first = Request([a.Artifact, b.Artifact], [c, a, b]);
        var second = Request([b.Artifact, a.Artifact], [b, c, a with { Requirements = a.Requirements.Reverse().ToArray() }]);

        Assert.Equal(JsonSerializer.Serialize(_planner.Plan(first)), JsonSerializer.Serialize(_planner.Plan(second)));
    }

    [Fact]
    public void DeferredModIdSeesCatalogDependencyExpandedLater()
    {
        var c = Evidence("c");
        var a = Evidence("a", Mod("c"));
        var b = Evidence("b", Required(c.Artifact));
        var plan = _planner.Plan(Request([a.Artifact, b.Artifact], [a, b, c]));

        Assert.True(plan.IsResolved);
        Assert.Equal(2, Item(plan, "c").Reasons.Count);
    }

    [Fact]
    public void SelfProvidedAliasIsNotMisidentifiedAsCatalogCycle()
    {
        var a = Evidence("a", Mod("alias"));
        a = a with { ProvidedMods = [.. a.ProvidedMods, Provided("alias", a.Artifact, ProvidedModKind.Alias)] };
        var plan = _planner.Plan(Request([a.Artifact], [a]));

        Assert.True(plan.IsResolved);
        Assert.Single(plan.Items);
    }

    [Fact]
    public void DepthLimitRejectsSeventeenthArtifactEntry()
    {
        var chain = Chain(17);
        Blocked(_planner.Plan(Request([chain[0].Artifact], chain)), "DepthLimit");
        var allowed = Chain(16);
        Assert.True(_planner.Plan(Request([allowed[0].Artifact], allowed)).IsResolved);
    }

    [Fact]
    public void TextualOptionalSuggestionReflectsExplicitVerifiedSelection()
    {
        var b = Evidence("b");
        var a = Evidence("a", Mod("b") with { Relation = DependencyRelation.Optional });
        var unselected = _planner.Plan(Request([a.Artifact], [a, b]));
        var selected = _planner.Plan(Request([a.Artifact, b.Artifact], [a, b]));
        Assert.False(Assert.Single(unselected.OptionalSuggestions).IsSelected);
        Assert.True(Assert.Single(selected.OptionalSuggestions).IsSelected);
    }

    [Fact]
    public void SharedNodeCannotHideOverdeepAlternativePath()
    {
        var chain = Chain(18);
        var root = Evidence("a", Required(chain[17].Artifact), Required(chain[0].Artifact));
        Blocked(_planner.Plan(Request([root.Artifact], [root, .. chain])), "DepthLimit");
    }

    [Fact]
    public void NodeAndRelationBudgetsFailClosed()
    {
        var tooMany = Enumerable.Range(0, 257).Select(i => Evidence($"p{i}")).ToArray();
        Blocked(_planner.Plan(Request([tooMany[0].Artifact], tooMany)), "NodeLimit");
        var a = Evidence("a") with { Requirements = Enumerable.Repeat(Mod("b"), 4097).ToArray() };
        Blocked(_planner.Plan(Request([a.Artifact], [a])), "RelationLimit");
    }

    [Fact]
    public void CancellationThrowsInsteadOfReturningAResolvedPartialPlan()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var a = Evidence("a");
        Assert.Throws<OperationCanceledException>(() => _planner.Plan(Request([a.Artifact], [a]), cancellation.Token));
    }

    [Fact]
    public void DuplicateEvidenceNeverSelectsFirstRecord()
    {
        var a = Evidence("a");
        Blocked(_planner.Plan(Request([a.Artifact], [a, a with { Requirements = [Mod("b")] }])), "AmbiguousEvidence");
    }

    [Fact]
    public void EmbeddedRelationRequiresVerifiedNestedDeclaration()
    {
        var a = Evidence("a", Mod("nested") with { Relation = DependencyRelation.Embedded });
        Blocked(_planner.Plan(Request([a.Artifact], [a])), "EmbeddedEvidenceMissing");
        a = a with { ProvidedMods = [.. a.ProvidedMods, Provided("nested", a.Artifact, ProvidedModKind.Nested)] };
        Assert.True(_planner.Plan(Request([a.Artifact], [a])).IsResolved);
    }

    [Fact]
    public void DifferentProviderProjectCannotCoexistForSameModId()
    {
        var a = Evidence("a");
        var b = Evidence("b");
        b = b with { ProvidedMods = [Provided("a", b.Artifact)] };
        Blocked(_planner.Plan(Request([a.Artifact, b.Artifact], [a, b])), "DuplicateModProvider");
    }

    [Fact]
    public void ExistingInventoryDependenciesAndPlatformConstraintsAreAlsoChecked()
    {
        var a = Evidence("a");
        var b = Evidence("b", Mod("missing"));
        Blocked(_planner.Plan(Request([a.Artifact], [a, b], [b.Artifact])), "UnresolvedModId");
        b = b with { Requirements = [Mod("java", "99")] };
        Blocked(_planner.Plan(Request([a.Artifact], [a, b], [b.Artifact])), "PlatformVersionMismatch");
    }

    [Theory]
    [InlineData("CON.jar")]
    [InlineData("NUL.jar")]
    [InlineData("COM1.jar")]
    [InlineData("lpt9.other.jar")]
    public void WindowsReservedFileNamesFailClosed(string name)
    {
        var a = Evidence("a");
        a = a with { Artifact = a.Artifact with { FileName = name } };
        Blocked(_planner.Plan(Request([a.Artifact], [a])), "InvalidIdentity");
    }

    [Fact]
    public void CaseInsensitiveFileNameCollisionFailsClosed()
    {
        var a = Evidence("a");
        var b = Evidence("b");
        a = a with { Artifact = a.Artifact with { FileName = "same.jar" } };
        b = b with { Artifact = b.Artifact with { FileName = "SAME.jar" } };
        Blocked(_planner.Plan(Request([a.Artifact, b.Artifact], [a, b])), "FileNameCollision");
    }

    [Fact]
    public void AggregateSizeCannotExceedTwoGiBEvenWhenEachFileFits()
    {
        var evidence = Enumerable.Range(0, 5).Select(i => Evidence($"p{i}")).Select(x =>
            x with { Artifact = x.Artifact with { SizeBytes = 512L * 1024 * 1024 }, VerifiedSizeBytes = 512L * 1024 * 1024 }).ToArray();
        Blocked(_planner.Plan(Request(evidence.Select(x => x.Artifact).ToArray(), evidence)), "TotalSizeLimit");
    }

    [Theory]
    [InlineData("latest", "latest", "latest")]
    [InlineData("1.21.1", "latest", "21")]
    [InlineData("1.21.1", "0.16.0", "latest")]
    [InlineData("01.21.1", "0.16.0", "21")]
    [InlineData("1.21.*", "0.16.0", "21")]
    public void RuntimeVersionsMustBeFixedNumericPins(string minecraft, string loader, string java)
    {
        var a = Evidence("a") with { MinecraftVersions = [minecraft] };
        var request = Request([a.Artifact], [a]) with { MinecraftVersion = minecraft, LoaderVersion = loader, JavaVersion = java };
        Blocked(_planner.Plan(request), "UnsupportedPlatform");
    }

    private static DependencyArtifactEvidence[] Chain(int count)
    {
        var chain = Enumerable.Range(0, count).Select(i => Evidence($"n{i:D3}")).ToArray();
        for (var i = 0; i < count - 1; i++) chain[i] = chain[i] with { Requirements = [Required(chain[i + 1].Artifact)] };
        return chain;
    }

    private static DependencyPlanningRequest Request(DependencyArtifactIdentity[] selected, DependencyArtifactEvidence[] evidence, DependencyArtifactIdentity[]? installed = null) => new()
    {
        MinecraftVersion = "1.21.1", Loader = "fabric", LoaderVersion = "0.16.0", JavaVersion = "21",
        SelectedArtifacts = selected, Evidence = evidence, InstalledArtifacts = installed ?? []
    };

    private static DependencyArtifactEvidence Evidence(string project, params DependencyRequirement[] requirements) => Evidence(project, requirements, "1");

    private static DependencyArtifactEvidence Evidence(string project, string version) => Evidence(project, [], version);

    private static DependencyArtifactEvidence Evidence(string project, DependencyRequirement[] requirements, string version)
    {
        var artifact = new DependencyArtifactIdentity
        {
            Provider = DependencyProvider.Modrinth, ProjectId = project, VersionId = version,
            FileName = $"{project}-{version}.jar", Sha256 = new string(version == "1" ? 'a' : 'b', 64), SizeBytes = 1234
        };
        return new()
        {
            Artifact = artifact, ApiStatus = DependencyEvidenceStatus.Verified, MetadataStatus = DependencyEvidenceStatus.Verified,
            DistributionAllowed = true, ApiFileSha256 = artifact.Sha256, InspectedContentSha256 = artifact.Sha256,
            VerifiedSizeBytes = artifact.SizeBytes, Loader = "fabric", Side = DependencySide.Universal,
            MinecraftVersions = ["1.21.1"], ProvidedMods = [Provided(project, artifact, version: version)], Requirements = requirements
        };
    }

    private static ProvidedModEvidence Provided(string modId, DependencyArtifactIdentity artifact, ProvidedModKind kind = ProvidedModKind.Primary, string version = "1") => new()
    {
        ModId = modId, Version = version, Kind = kind, Side = DependencySide.Universal,
        Status = DependencyEvidenceStatus.Verified, ContentSha256 = artifact.Sha256
    };

    private static DependencyRequirement Required(DependencyArtifactIdentity artifact) => new()
    {
        Artifact = artifact, ConstraintKind = DependencyConstraintKind.Exact,
        Relation = DependencyRelation.Required, Source = DependencyEvidenceSource.ProviderApi, OriginalRelation = "required"
    };

    private static DependencyRequirement Mod(string id, string version = "1") => new()
    {
        ModId = id, VersionConstraint = version, ConstraintKind = DependencyConstraintKind.Exact,
        Relation = DependencyRelation.Required, Source = DependencyEvidenceSource.FabricMetadata, OriginalRelation = "depends"
    };

    private static VerifiedModBinding Binding(DependencyArtifactEvidence evidence) => new()
    {
        ModId = evidence.ProvidedMods[0].ModId, Version = evidence.ProvidedMods[0].Version,
        Artifact = evidence.Artifact, Status = DependencyEvidenceStatus.Verified, EvidenceContentSha256 = evidence.Artifact.Sha256
    };

    private static DependencyPlanItem Item(DependencyPlan plan, string project) => Assert.Single(plan.Items.Where(x => x.Artifact.ProjectId == project));
    private static void Blocked(DependencyPlan plan, string code)
    {
        Assert.False(plan.IsResolved);
        Assert.Contains(plan.Diagnostics, x => x.Code == code && x.Severity == DependencyDiagnosticSeverity.Blocker);
    }
}
