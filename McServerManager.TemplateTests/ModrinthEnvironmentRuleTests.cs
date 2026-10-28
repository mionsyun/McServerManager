using System.IO.Compression;
using System.Text.Json;
using McServerManager.Models.Modrinth;
using McServerManager.Services.Modrinth;

namespace McServerManager.TemplateTests;

public sealed class ModrinthEnvironmentRuleTests
{
    [Fact]
    public async Task VerifiedLoaderProfileSoftensOnlyPresentMatchingDisabledId()
    {
        var fixture = Fixture();
        AddBundle(fixture, Module("render_api", "2.0.0", "client"));
        using var provider = fixture.Provider();
        var result = await new ModrinthInspectionService(provider).InspectAsync(Request());
        Assert.True(result.IsResolved);
        Assert.Contains(result.Findings, f => f.Code == "EnvironmentDisabledDependency" && f.Dependency == "render_api");
        Assert.DoesNotContain(result.Findings, f => f.Code == "RequiredSatisfied" && f.Dependency == "render_api");
    }

    [Theory]
    [InlineData("0.18.4")]
    [InlineData("0.19.6")]
    [InlineData("0.19.5+unverified")]
    public async Task UnreviewedLoaderProfileDoesNotSilentlyApplyRule(string loader)
    {
        var fixture = Fixture();
        AddBundle(fixture, Module("render_api", "2.0.0", "client"));
        using var provider = fixture.Provider();
        var result = await new ModrinthInspectionService(provider).InspectAsync(Request() with { LoaderVersion = loader });
        Assert.False(result.IsResolved);
        Assert.Contains(result.Findings, f => loader.Contains('+') ? f.Code == "InvalidTarget" : f.Code == "MissingRequiredDependency" && f.Dependency == "render_api");
    }

    [Theory]
    [InlineData("1.0.0", "render_api", null)]
    [InlineData("2.0.0", "another_api", null)]
    [InlineData("2.0.0", "another_api", "render_api")]
    public async Task WrongVersionAbsentIdAndDisabledAliasCannotSoften(string version, string id, string? alias)
    {
        var fixture = Fixture();
        AddBundle(fixture, Module(id, version, "client", alias));
        using var provider = fixture.Provider();
        var result = await new ModrinthInspectionService(provider).InspectAsync(Request());
        Assert.False(result.IsResolved);
        Assert.Contains(result.Findings, f => f.Code == "MissingRequiredDependency" && f.Dependency == "render_api");
        Assert.DoesNotContain(result.Findings, f => f.Code == "EnvironmentDisabledDependency");
    }

    [Fact]
    public async Task ActiveWrongVersionTakesPrecedenceOverMatchingDisabledCandidate()
    {
        var fixture = Fixture();
        AddBundle(fixture, Module("render_api", "2.0.0", "client"));
        fixture.Add("ActivePr", "ActiveV1", "render_api", "1.0.0", depends: new { });
        using var provider = fixture.Provider();
        var result = await new ModrinthInspectionService(provider).InspectAsync(Request("ActiveV1"));
        Assert.False(result.IsResolved);
        Assert.Contains(result.Findings, f => f.Code == "VersionMismatch" && f.Dependency == "render_api");
        Assert.DoesNotContain(result.Findings, f => f.Code == "EnvironmentDisabledDependency");
    }

    [Fact]
    public async Task ClientParentHidesItsNestedCandidateFromDisabledDiscovery()
    {
        var fixture = Fixture();
        var clientParent = Bundle("client_parent", "client", Module("render_api", "2.0.0", "client"));
        AddBundle(fixture, clientParent);
        using var provider = fixture.Provider();
        var result = await new ModrinthInspectionService(provider).InspectAsync(Request());
        Assert.False(result.IsResolved);
        Assert.Contains(result.Findings, f => f.Code == "MissingRequiredDependency" && f.Dependency == "render_api");
    }

    [Fact]
    public async Task UnknownConstraintIsNotSoftened()
    {
        var fixture = Fixture("[1.0,3.0)");
        AddBundle(fixture, Module("render_api", "2.0.0", "client"));
        using var provider = fixture.Provider();
        var result = await new ModrinthInspectionService(provider).InspectAsync(Request());
        Assert.False(result.IsResolved);
        Assert.Contains(result.Findings, f => f.Code == "UnknownVersionConstraint");
        Assert.DoesNotContain(result.Findings, f => f.Code == "EnvironmentDisabledDependency");
    }

    [Fact]
    public async Task EnvironmentRuleDoesNotRelaxApiRequiredProject()
    {
        var fixture = Fixture(apiRequired: true);
        AddBundle(fixture, Module("render_api", "2.0.0", "client"));
        using var provider = fixture.Provider();
        var result = await new ModrinthInspectionService(provider).InspectAsync(Request());
        Assert.False(result.IsResolved);
        Assert.Contains(result.Findings, f => f.Code == "EnvironmentDisabledDependency");
        Assert.Contains(result.Findings, f => f.Code == "UnresolvedProjectDependency" && f.Dependency == "MissingP");
    }

    [Fact]
    public async Task DisabledRootRemainsRejectedEvenWhenDependencyIsSoftened()
    {
        var fixture = Fixture();
        fixture.Add("RootClnt", "ClientV1", "render_api", "2.0.0", environment: "client", depends: new { });
        using var provider = fixture.Provider();
        var request = Request() with { SelectedPins = [new() { VersionId = "Consumer" }, new() { VersionId = "ClientV1" }] };
        var result = await new ModrinthInspectionService(provider).InspectAsync(request);
        Assert.False(result.IsResolved);
        Assert.Contains(result.Findings, f => f.Code == "ClientOnly");
    }

    private static ModrinthHttpFixture Fixture(string range = ">=2.0.0", bool apiRequired = false)
    {
        var fixture = new ModrinthHttpFixture();
        fixture.Add("Consumer", "Consumer", "consumer", "1.0.0", depends: new { render_api = range },
            dependencies: apiRequired ? [new { project_id = "MissingP", version_id = (string?)null, file_name = (string?)null, dependency_type = "required" }] : []);
        return fixture;
    }
    private static ModrinthInspectionRequest Request(params string[] extra) => new()
    {
        MinecraftVersion = "1.21.1", LoaderVersion = "0.19.5", JavaVersion = "21",
        SelectedPins = new[] { "Consumer", "BundleV1" }.Concat(extra).Select(v => new ModrinthPin { VersionId = v }).ToArray()
    };
    private static void AddBundle(ModrinthHttpFixture fixture, byte[] child) =>
        fixture.Add("BundlePr", "BundleV1", "bundle", "1.0.0", content: Bundle("bundle", "*", child));
    private static byte[] Module(string id, string version, string environment, string? alias = null) =>
        ModrinthHttpFixture.Jar(JsonSerializer.Serialize(new { schemaVersion = 1, id, version, environment, provides = alias is null ? Array.Empty<string>() : new[] { alias } }));
    private static byte[] Bundle(string id, string environment, byte[] child)
    {
        using var bytes = new MemoryStream();
        using (var zip = new ZipArchive(bytes, ZipArchiveMode.Create, leaveOpen: true))
        {
            using (var writer = new StreamWriter(zip.CreateEntry("fabric.mod.json").Open()))
                writer.Write(JsonSerializer.Serialize(new { schemaVersion = 1, id, version = "1.0.0", environment, jars = new[] { new { file = "META-INF/jars/child.jar" } } }));
            using var output = zip.CreateEntry("META-INF/jars/child.jar").Open();
            output.Write(child);
        }
        return bytes.ToArray();
    }
}
