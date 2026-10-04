using System.IO.Compression;
using System.Text.Json;
using McServerManager.Models.Modrinth;
using McServerManager.Services.Modrinth;

namespace McServerManager.TemplateTests;

public sealed class ModrinthInspectionServiceTests
{
    [Fact]
    public async Task ProjectOnlyDependencyIsUnresolvedWithoutGuessingLatest()
    {
        var fixture = RootWithLibrary();
        using var provider = fixture.Provider();
        var result = await new ModrinthInspectionService(provider).InspectAsync(Request("RootVer1"));
        Assert.False(result.IsResolved);
        Assert.Contains(result.Findings, x => x.Code == "UnresolvedProjectDependency" && x.Dependency == "Library1");
        Assert.Contains(result.Findings, x => x.Code == "MissingRequiredDependency" && x.Dependency == "librarymod");
        Assert.DoesNotContain(fixture.Requests, x => x.Url.Contains("search", StringComparison.Ordinal) || x.Url.Contains("latest", StringComparison.Ordinal));
        Assert.Single(result.Artifacts);
    }

    [Fact]
    public async Task ExplicitProjectPinBindsActualJarIdAndResolvesRequiredDependency()
    {
        var fixture = RootWithLibrary();
        using var provider = fixture.Provider();
        var request = Request("RootVer1") with { DependencyPins = [new() { VersionId = "LibVers1" }] };
        var result = await new ModrinthInspectionService(provider).InspectAsync(request);
        Assert.True(result.IsResolved, string.Join("\n", result.Findings.Where(x => x.Severity == ModrinthFindingSeverity.Blocker).Select(x => x.Message)));
        Assert.Equal(2, result.Artifacts.Count);
        Assert.Contains(result.Findings, x => x.Code == "TransitiveRequired" && x.Dependency == "Library1");
        Assert.Contains(result.Findings, x => x.Code == "RequiredSatisfied" && x.Dependency == "librarymod");
        Assert.All(result.Artifacts, x => Assert.Equal(x.Sha256, x.Metadata.Root!.ContentSha256));
    }

    [Fact]
    public async Task ExactApiVersionRecursesTransitivelyWithActualMetadata()
    {
        var fixture = new ModrinthHttpFixture();
        fixture.Add("RootProj", "RootVer1", "rootmod", "1.0.0", depends: new { middlemod = ">=1" }, dependencies: [Edge("Middle01", "MiddleV1")]);
        fixture.Add("Middle01", "MiddleV1", "middlemod", "1.0.0", depends: new { librarymod = ">=1" }, dependencies: [Edge("Library1", "LibVers1")]);
        fixture.Add("Library1", "LibVers1", "librarymod", "1.0.0");
        using var provider = fixture.Provider();
        var result = await new ModrinthInspectionService(provider).InspectAsync(Request("RootVer1"));
        Assert.True(result.IsResolved);
        Assert.Equal(3, result.Artifacts.Count);
        Assert.Equal(new[] { "RootVer1", "MiddleV1", "LibVers1" }, result.Artifacts.Single(x => x.Version!.Id == "LibVers1").Chain);
    }

    [Fact]
    public async Task OptionalDependenciesArePresentedButNeverAcquired()
    {
        var fixture = new ModrinthHttpFixture();
        fixture.Add("RootProj", "RootVer1", "rootmod", "1.0.0", suggests: new { optionalmod = "*" }, dependencies: [Edge("Optional", "Optional", "optional")]);
        using var provider = fixture.Provider();
        var result = await new ModrinthInspectionService(provider).InspectAsync(Request("RootVer1"));
        Assert.True(result.IsResolved);
        Assert.Equal(2, result.Findings.Count(x => x.Code == "OptionalSuggestion"));
        Assert.DoesNotContain(fixture.Requests, x => x.Url.Contains("Optional", StringComparison.Ordinal));
    }

    [Fact]
    public async Task RealInstalledBytesSatisfyDependencyWithoutDownloadingTheInstalledFile()
    {
        var fixture = RootWithLibrary();
        var bytes = fixture.Add("Library1", "LibVers1", "librarymod", "1.2.0");
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".jar");
        await File.WriteAllBytesAsync(path, bytes);
        try
        {
            using var provider = fixture.Provider();
            var request = Request("RootVer1") with { InstalledLocalFiles = [path] };
            var result = await new ModrinthInspectionService(provider).InspectAsync(request);
            Assert.True(result.IsResolved);
            Assert.Contains(result.Findings, x => x.Code == "AlreadyInstalled" && x.Dependency == "librarymod");
            Assert.DoesNotContain(fixture.Requests, x => x.Url == ModrinthHttpFixture.FileUrl("Library1", "LibVers1", "librarymod.jar"));
            Assert.Equal(bytes, await File.ReadAllBytesAsync(path));
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public async Task InstalledVersionMismatchIsBlockedAndLocalFileIsUntouched()
    {
        var fixture = new ModrinthHttpFixture();
        fixture.Add("RootProj", "RootVer1", "rootmod", "1.0.0", depends: new { librarymod = ">=2" }, dependencies: [Edge("Library1")]);
        var bytes = fixture.Add("Library1", "LibVers1", "librarymod", "1.0.0");
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".jar");
        await File.WriteAllBytesAsync(path, bytes);
        try
        {
            using var provider = fixture.Provider();
            var result = await new ModrinthInspectionService(provider).InspectAsync(Request("RootVer1") with { InstalledLocalFiles = [path] });
            Assert.False(result.IsResolved);
            Assert.Contains(result.Findings, x => x.Code == "InstalledVersionMismatch");
            Assert.Equal(bytes, await File.ReadAllBytesAsync(path));
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public async Task BothApiIncompatibilityAndFabricBreaksArePreserved()
    {
        var fixture = new ModrinthHttpFixture();
        fixture.Add("RootProj", "RootVer1", "rootmod", "1.0.0", breaks: new { librarymod = "*" }, dependencies: [Edge("Library1", type: "incompatible")]);
        fixture.Add("Library1", "LibVers1", "librarymod", "1.0.0");
        using var provider = fixture.Provider();
        var result = await new ModrinthInspectionService(provider).InspectAsync(Request("RootVer1", "LibVers1"));
        Assert.False(result.IsResolved);
        Assert.Contains(result.Findings, x => x.Code == "Conflict" && x.Source.StartsWith("https://api.modrinth.com", StringComparison.Ordinal));
        Assert.Contains(result.Findings, x => x.Code == "Conflict" && x.Source.StartsWith("fabric.mod.json", StringComparison.Ordinal));
    }

    [Fact]
    public async Task UnsupportedConstraintNeverBecomesCompatible()
    {
        var fixture = new ModrinthHttpFixture();
        fixture.Add("RootProj", "RootVer1", "rootmod", "1.0.0", depends: new { librarymod = ">=1 || <3" });
        fixture.Add("Library1", "LibVers1", "librarymod", "1.0.0");
        using var provider = fixture.Provider();
        var result = await new ModrinthInspectionService(provider).InspectAsync(Request("RootVer1", "LibVers1"));
        Assert.False(result.IsResolved);
        Assert.Contains(result.Findings, x => x.Code == "UnknownVersionConstraint");
    }

    [Fact]
    public async Task TemplateHashAndProviderIdentityAssertionsAreEnforced()
    {
        var fixture = new ModrinthHttpFixture(); fixture.Add("RootProj", "RootVer1", "rootmod", "1.0.0");
        using var provider = fixture.Provider();
        var request = Request("RootVer1") with { SelectedPins = [new() { VersionId = "RootVer1", Sha256 = new('a', 64) }] };
        var result = await new ModrinthInspectionService(provider).InspectAsync(request);
        Assert.False(result.IsResolved); Assert.Contains(result.Findings, x => x.Code == "PinHashMismatch");
        var wrongProject = Request("RootVer1") with { SelectedPins = [new() { VersionId = "RootVer1", ProjectId = "Other111" }] };
        var second = await new ModrinthInspectionService(provider).InspectAsync(wrongProject);
        Assert.False(second.IsResolved); Assert.Contains(second.Findings, x => x.Code == "PinMismatch");
    }

    [Fact]
    public async Task NullIdsAndUnsupportedTargetsReturnBlockingFindings()
    {
        var fixture = new ModrinthHttpFixture(); using var provider = fixture.Provider();
        var service = new ModrinthInspectionService(provider);
        var result = await service.InspectAsync(Request("RootVer1") with { SelectedPins = [new() { VersionId = null! }] });
        Assert.False(result.IsResolved); Assert.Contains(result.Findings, x => x.Code == "InvalidId");
        var target = await service.InspectAsync(Request("RootVer1") with { LoaderVersion = "latest" });
        Assert.False(target.IsResolved); Assert.Contains(target.Findings, x => x.Code == "InvalidTarget");
        Assert.Empty(fixture.Requests);
    }

    [Fact]
    public async Task ServiceRehashesBytesEvenWhenProviderClaimsCorrectHashes()
    {
        var fixture = new ModrinthHttpFixture(); fixture.Add("RootProj", "RootVer1", "rootmod", "1.0.0");
        using var provider = fixture.Provider();
        var result = await new ModrinthInspectionService(new CorruptingProvider(provider)).InspectAsync(Request("RootVer1"));
        Assert.False(result.IsResolved);
        Assert.Contains(result.Findings, x => x.Code == "HashMismatch");
        Assert.Empty(result.Artifacts);
    }

    [Fact]
    public async Task UniversalGrandchildInsideClientParentCannotSatisfyServerDependency()
    {
        var grandchild = ModrinthHttpFixture.Jar("""{"schemaVersion":1,"id":"grandchild","version":"1.0.0","environment":"*"}""");
        var client = NestedJar("""{"schemaVersion":1,"id":"clientmodule","version":"1.0.0","environment":"client","jars":[{"file":"grandchild.jar"}]}""", "grandchild.jar", grandchild);
        var root = NestedJar("""{"schemaVersion":1,"id":"rootmod","version":"1.0.0","environment":"*","depends":{"grandchild":"*"},"jars":[{"file":"client.jar"}]}""", "client.jar", client);
        var fixture = new ModrinthHttpFixture(); fixture.Add("RootProj", "RootVer1", "rootmod", "1.0.0", content: root);
        using var provider = fixture.Provider();
        var result = await new ModrinthInspectionService(provider).InspectAsync(Request("RootVer1"));
        Assert.Equal(3, result.Artifacts.Single().Metadata.Mods.Count);
        Assert.False(result.IsResolved);
        Assert.Contains(result.Findings, x => x.Code == "MissingRequiredDependency" && x.Dependency == "grandchild");
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task IdenticalNestedCandidatesKeepServerApplicabilityPerAncestry(bool allParentsClient, bool reverseOrder)
    {
        var shared = ModrinthHttpFixture.Jar("""{"schemaVersion":1,"id":"grandchild","version":"1.0.0","provides":["shared_alias"],"depends":{"minecraft":"1.21.1"}}""");
        var client = NestedJar("""{"schemaVersion":1,"id":"clientmodule","version":"1.0.0","environment":"client","jars":[{"file":"shared.jar"}]}""", "shared.jar", shared);
        var second = NestedJar(JsonSerializer.Serialize(new { schemaVersion = 1, id = "secondmodule", version = "1.0.0", environment = allParentsClient ? "client" : "*",
            jars = new[] { new { file = "shared.jar" } } }), "shared.jar", shared);
        var children = new[] { ("client.jar", client), ("second.jar", second) };
        if (reverseOrder) Array.Reverse(children);
        var root = BundledJar("rootmod", new { grandchild = "1.0.0", shared_alias = "*" }, children);
        var fixture = new ModrinthHttpFixture(); fixture.Add("RootProj", "RootVer1", "rootmod", "1.0.0", content: root);
        using var provider = fixture.Provider();
        var result = await new ModrinthInspectionService(provider).InspectAsync(Request("RootVer1"));

        var metadata = Assert.Single(result.Artifacts).Metadata;
        Assert.True(metadata.IsComplete);
        Assert.Equal(5, metadata.Mods.Count);
        var copies = metadata.Mods.Where(x => x.Id == "grandchild").ToArray();
        Assert.Equal(2, copies.Length);
        Assert.Equal(copies[0].ContentSha256, copies[1].ContentSha256);
        Assert.NotEqual(copies[0].ArchivePath, copies[1].ArchivePath);
        Assert.Equal(!allParentsClient, result.IsResolved);
        foreach (var dependency in new[] { "grandchild", "shared_alias" })
        {
            Assert.Contains(result.Findings, x => x.Dependency == dependency && x.Code == (allParentsClient ? "MissingRequiredDependency" : "RequiredSatisfied"));
            Assert.DoesNotContain(result.Findings, x => x.Dependency == dependency && x.Code == (allParentsClient ? "RequiredSatisfied" : "MissingRequiredDependency"));
        }
        var sharedRuntimeChecks = result.Findings.Where(x => x.Dependency == "minecraft" && x.Code == "RuntimeSatisfied").ToArray();
        if (allParentsClient) Assert.Empty(sharedRuntimeChecks);
        else Assert.EndsWith("!/second.jar!/shared.jar", Assert.Single(sharedRuntimeChecks).Source);
        Assert.DoesNotContain(result.Findings, x => x.Code is "DuplicateModId" or "UnsupportedNestedAlternatives");
    }

    [Fact]
    public async Task IdenticalNestedContentAtSiblingAndGrandchildPathsSuppliesOneProvider()
    {
        var shared = ModrinthHttpFixture.Jar("""{"schemaVersion":1,"id":"sharedmod","version":"1.0.0"}""");
        var parent = NestedJar("""{"schemaVersion":1,"id":"parentmod","version":"1.0.0","depends":{"sharedmod":"1.0.0"},"jars":[{"file":"shared.jar"}]}""", "shared.jar", shared);
        var root = BundledJar("rootmod", new { sharedmod = "1.0.0" }, ("shared.jar", shared), ("parent.jar", parent));
        var fixture = new ModrinthHttpFixture(); fixture.Add("RootProj", "RootVer1", "rootmod", "1.0.0", content: root);
        using var provider = fixture.Provider();
        var result = await new ModrinthInspectionService(provider).InspectAsync(Request("RootVer1"));

        Assert.True(result.IsResolved);
        Assert.Equal(4, Assert.Single(result.Artifacts).Metadata.Mods.Count);
        Assert.Equal(2, result.Findings.Count(x => x.Code == "RequiredSatisfied" && x.Dependency == "sharedmod"));
        Assert.DoesNotContain(result.Findings, x => x.Code is "DuplicateModId" or "UnsupportedNestedAlternatives");
    }

    [Theory]
    [InlineData("1.0.0")]
    [InlineData("2.0.0")]
    public async Task DifferentNestedBytesAcrossArtifactsRemainUnsupportedAlternatives(string otherVersion)
    {
        var first = ModrinthHttpFixture.Jar("""{"schemaVersion":1,"id":"sharedmod","version":"1.0.0","description":"first"}""");
        var other = ModrinthHttpFixture.Jar(JsonSerializer.Serialize(new { schemaVersion = 1, id = "sharedmod", version = otherVersion, description = "other" }));
        var fixture = new ModrinthHttpFixture();
        fixture.Add("RootProj", "RootVer1", "rootmod", "1.0.0", content: BundledJar("rootmod", new { sharedmod = "*" }, ("shared.jar", first)));
        fixture.Add("OtherPro", "OtherVer", "othermod", "1.0.0", content: BundledJar("othermod", new { }, ("shared.jar", other)));
        using var provider = fixture.Provider();
        var result = await new ModrinthInspectionService(provider).InspectAsync(Request("RootVer1", "OtherVer"));

        Assert.False(result.IsResolved);
        Assert.All(result.Artifacts, x => Assert.True(x.Metadata.IsComplete));
        Assert.Contains(result.Findings, x => x.Code == "UnsupportedNestedAlternatives" && x.Dependency == "sharedmod");
        Assert.DoesNotContain(result.Findings, x => x.Code == "RequiredSatisfied" && x.Dependency == "sharedmod");
    }

    [Fact]
    public async Task RootAliasesAndActualNestedProvidersSatisfyDependencies()
    {
        var nested = ModrinthHttpFixture.Jar("""{"schemaVersion":1,"id":"nestedmodule","version":"1.0.0"}""");
        var bundle = NestedJar("""{"schemaVersion":1,"id":"bundlemod","version":"1.0.0","provides":["bundle_alias"],"jars":[{"file":"nested.jar"}]}""", "nested.jar", nested);
        var fixture = new ModrinthHttpFixture();
        fixture.Add("RootProj", "RootVer1", "rootmod", "1.0.0", depends: new { bundle_alias = ">=1", nestedmodule = "1.0.0" });
        fixture.Add("Bundle01", "BundleV1", "bundlemod", "1.0.0", content: bundle);
        using var provider = fixture.Provider();
        var result = await new ModrinthInspectionService(provider).InspectAsync(Request("RootVer1", "BundleV1"));
        Assert.True(result.IsResolved);
        Assert.Contains(result.Findings, x => x.Code == "RequiredSatisfied" && x.Dependency == "bundle_alias");
        Assert.Contains(result.Findings, x => x.Code == "RequiredSatisfied" && x.Dependency == "nestedmodule");
    }

    [Fact]
    public async Task CancellationDoesNotYieldAPartialSuccess()
    {
        var fixture = new ModrinthHttpFixture(); using var provider = fixture.Provider();
        using var ct = new CancellationTokenSource(); ct.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => new ModrinthInspectionService(provider).InspectAsync(Request("RootVer1"), ct.Token));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task DistinctRootArtifactsWithIdenticalBytesRemainDuplicateProviders(bool repeatedNestedContent)
    {
        var bytes = ModrinthHttpFixture.Jar("""{"schemaVersion":1,"id":"same_root","version":"1.0.0"}""");
        if (repeatedNestedContent)
        {
            var shared = ModrinthHttpFixture.Jar("""{"schemaVersion":1,"id":"sharedmod","version":"1.0.0"}""");
            bytes = BundledJar("same_root", new { sharedmod = "*" }, ("first.jar", shared), ("second.jar", shared));
        }
        var fixture = new ModrinthHttpFixture();
        fixture.Add("RootProj", "RootVer1", "same_root", "1.0.0", content: bytes);
        fixture.Add("OtherPro", "OtherVer", "same_root", "1.0.0", content: bytes);
        using var provider = fixture.Provider();
        var result = await new ModrinthInspectionService(provider).InspectAsync(Request("RootVer1", "OtherVer"));
        Assert.False(result.IsResolved);
        Assert.All(result.Artifacts, x => Assert.True(x.Metadata.IsComplete));
        Assert.Contains(result.Findings, x => x.Code == "DuplicateModId" && x.Dependency == "same_root");
        Assert.DoesNotContain(result.Findings, x => x.Code == "UnsupportedNestedAlternatives");
    }

    [Fact]
    public async Task IdenticalNestedOccurrencesStillConsumeMetadataRegistrationBudget()
    {
        var shared = ModrinthHttpFixture.Jar(JsonSerializer.Serialize(new { schemaVersion = 1, id = "sharedmod", version = "1.0.0",
            provides = Enumerable.Range(0, 256).Select(x => $"shared_alias_{x}").ToArray() }));
        var root = BundledJar("rootmod", new { }, Enumerable.Range(0, 17).Select(x => ($"shared-{x}.jar", shared)).ToArray());
        var fixture = new ModrinthHttpFixture(); fixture.Add("RootProj", "RootVer1", "rootmod", "1.0.0", content: root);
        using var provider = fixture.Provider();
        var result = await new ModrinthInspectionService(provider).InspectAsync(Request("RootVer1"));

        Assert.False(result.IsResolved);
        var metadata = Assert.Single(result.Artifacts).Metadata;
        Assert.True(metadata.IsComplete);
        Assert.Equal(18, metadata.Mods.Count);
        Assert.Contains(result.Findings, x => x.Code == "MetadataLimit");
        Assert.DoesNotContain(result.Findings, x => x.Code is "DuplicateModId" or "UnsupportedNestedAlternatives");
    }

    [Fact]
    public async Task MetadataAliasBudgetIsEnforcedBeforeProviderMapConstruction()
    {
        var fixture = new ModrinthHttpFixture();
        var versions = new List<string>();
        for (var i = 0; i < 17; i++)
        {
            var bytes = ModrinthHttpFixture.Jar(JsonSerializer.Serialize(new { schemaVersion = 1, id = "root_" + i, version = "1.0.0",
                provides = Enumerable.Range(0, 256).Select(x => $"alias_{i}_{x}").ToArray() }));
            versions.Add("Version" + i);
            fixture.Add("Project" + i, "Version" + i, "root_" + i, "1.0.0", content: bytes);
        }
        using var provider = fixture.Provider();
        var result = await new ModrinthInspectionService(provider).InspectAsync(Request(versions.ToArray()));
        Assert.False(result.IsResolved);
        Assert.Contains(result.Findings, x => x.Code == "MetadataLimit");
    }

    [Fact]
    public async Task CyclesAreDetectedEvenWhenBothArtifactsWereSelectedAsRoots()
    {
        var fixture = new ModrinthHttpFixture();
        fixture.Add("RootProj", "RootVer1", "rootmod", "1.0.0", dependencies: [Edge("Library1", "LibVers1")]);
        fixture.Add("Library1", "LibVers1", "librarymod", "1.0.0", dependencies: [Edge("RootProj", "RootVer1")]);
        using var provider = fixture.Provider();
        var result = await new ModrinthInspectionService(provider).InspectAsync(Request("RootVer1", "LibVers1"));
        Assert.False(result.IsResolved);
        Assert.Contains(result.Findings, x => x.Code == "DependencyCycle");
    }

    [Fact]
    public async Task FilenameOnlyIncompatibilityRemainsAnUnresolvedBlocker()
    {
        var fixture = new ModrinthHttpFixture();
        fixture.Add("RootProj", "RootVer1", "rootmod", "1.0.0", dependencies:
            [new { project_id = (string?)null, version_id = (string?)null, file_name = "other.jar", dependency_type = "incompatible" }]);
        using var provider = fixture.Provider();
        var result = await new ModrinthInspectionService(provider).InspectAsync(Request("RootVer1"));
        Assert.False(result.IsResolved);
        Assert.Contains(result.Findings, x => x.Code == "UnresolvedConflict");
    }

    [Theory]
    [InlineData(".hidden.jar")]
    [InlineData("helper.JAR")]
    public async Task LoaderUndiscoverableLocalFilesCannotSatisfyInstalledDependencies(string filename)
    {
        var fixture = RootWithLibrary();
        var bytes = fixture.Add("Library1", "LibVers1", "librarymod", "1.2.0");
        var directory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, filename);
        await File.WriteAllBytesAsync(path, bytes);
        try
        {
            using var provider = fixture.Provider();
            var result = await new ModrinthInspectionService(provider).InspectAsync(Request("RootVer1") with { InstalledLocalFiles = [path] });
            Assert.False(result.IsResolved);
            Assert.Contains(result.Findings, x => x.Code == "UndiscoverableRootFile");
            Assert.Contains(result.Findings, x => x.Code == "MissingRequiredDependency" && x.Dependency == "librarymod");
            Assert.DoesNotContain(result.Findings, x => x.Code == "AlreadyInstalled");
            Assert.DoesNotContain(result.Artifacts, x => x.IsInstalled);
            Assert.Equal(bytes, await File.ReadAllBytesAsync(path));
        }
        finally { File.Delete(path); Directory.Delete(directory); }
    }

    private static ModrinthHttpFixture RootWithLibrary()
    {
        var fixture = new ModrinthHttpFixture();
        fixture.Add("RootProj", "RootVer1", "rootmod", "1.0.0", depends: new { librarymod = ">=1.2.0", minecraft = "1.21.1" }, dependencies: [Edge("Library1")]);
        fixture.Add("Library1", "LibVers1", "librarymod", "1.2.0");
        return fixture;
    }
    private static ModrinthInspectionRequest Request(params string[] versions) => new()
    { MinecraftVersion = "1.21.1", LoaderVersion = "0.16.10", JavaVersion = "21", SelectedPins = versions.Select(x => new ModrinthPin { VersionId = x }).ToArray() };
    private static object Edge(string project, string? version = null, string type = "required") => new { project_id = project, version_id = version, file_name = (string?)null, dependency_type = type };
    private static byte[] NestedJar(string metadata, string nestedName, byte[] nested)
    {
        using var bytes = new MemoryStream();
        using (var zip = new ZipArchive(bytes, ZipArchiveMode.Create, leaveOpen: true))
        {
            using (var writer = new StreamWriter(zip.CreateEntry("fabric.mod.json").Open())) writer.Write(metadata);
            using (var stream = zip.CreateEntry(nestedName).Open()) stream.Write(nested);
        }
        return bytes.ToArray();
    }
    private static byte[] BundledJar(string id, object depends, params (string Name, byte[] Content)[] nested)
    {
        var metadata = JsonSerializer.Serialize(new { schemaVersion = 1, id, version = "1.0.0", depends,
            jars = nested.Select(x => new { file = x.Name }).ToArray() });
        using var bytes = new MemoryStream();
        using (var zip = new ZipArchive(bytes, ZipArchiveMode.Create, leaveOpen: true))
        {
            using (var writer = new StreamWriter(zip.CreateEntry("fabric.mod.json", CompressionLevel.NoCompression).Open())) writer.Write(metadata);
            foreach (var (name, content) in nested)
            {
                using var stream = zip.CreateEntry(name, CompressionLevel.NoCompression).Open();
                stream.Write(content);
            }
        }
        return bytes.ToArray();
    }
    private sealed class CorruptingProvider(IModrinthProvider inner) : IModrinthProvider
    {
        public Task<ModrinthVersion> GetVersionAsync(string id, CancellationToken ct = default) => inner.GetVersionAsync(id, ct);
        public Task<ModrinthVersion?> GetVersionByHashAsync(string hash, CancellationToken ct = default) => inner.GetVersionByHashAsync(hash, ct);
        public Task<ModrinthProject> GetProjectAsync(string id, CancellationToken ct = default) => inner.GetProjectAsync(id, ct);
        public async Task<ModrinthDownload> DownloadAsync(ModrinthVersion version, ModrinthFile file, CancellationToken ct = default)
        {
            var result = await inner.DownloadAsync(version, file, ct);
            var bytes = result.Bytes.ToArray(); bytes[0] ^= 1;
            return result with { Bytes = bytes };
        }
    }
}
