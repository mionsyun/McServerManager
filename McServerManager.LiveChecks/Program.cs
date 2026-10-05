using System.Text.Json;
using System.Text.Json.Serialization;
using McServerManager.Models.Modrinth;
using McServerManager.Services.Fabric;
using McServerManager.Services.Modrinth;

if (args.Length != 3 || args[0] != "--live")
{
    System.Console.Error.WriteLine("Usage: --live cases.json report.json (downloads public MOD files for non-executing inspection)");
    return 2;
}
var json = new JsonSerializerOptions { WriteIndented = true, PropertyNameCaseInsensitive = true };
json.Converters.Add(new JsonStringEnumConverter());
var cases = JsonSerializer.Deserialize<List<LiveCase>>(await File.ReadAllTextAsync(args[1]), json)
    ?? throw new InvalidDataException("No cases supplied.");
if (cases.Count is < 1 or > 40) throw new InvalidDataException("Supply 1–40 explicit cases.");
using var provider = new ModrinthProvider();
var service = new ModrinthInspectionService(provider, new FabricJarInspector(), new FabricVersionMatcher());
var reports = new List<object>();
var failures = 0;
foreach (var test in cases)
{
    var started = DateTimeOffset.UtcNow;
    var result = await service.InspectAsync(test.Request);
    var codes = result.Findings.Select(f => f.Code).Distinct().ToArray();
    var actualVersions = result.Artifacts.Select(a => a.Version?.Id ?? "unknown").Order(StringComparer.Ordinal).ToArray();
    var passed = result.IsResolved == test.ExpectedResolved && test.ExpectedFindingCodes.All(codes.Contains)
        && test.ExpectedFindings.All(expected => result.Findings.Any(f => f.Code == expected.Code && f.Dependency == expected.Dependency))
        && (test.ExpectedVersionIds is null || actualVersions.SequenceEqual(test.ExpectedVersionIds.Order(StringComparer.Ordinal)))
        && (test.ExpectedInstalledCount is null || result.Artifacts.Count(a => a.IsInstalled) == test.ExpectedInstalledCount);
    if (!passed) failures++;
    reports.Add(new
    {
        test.Name, StartedAt = started, CompletedAt = DateTimeOffset.UtcNow, test.ExpectedResolved,
        ActualResolved = result.IsResolved, Passed = passed, test.ExpectedFindingCodes, test.ExpectedFindings, test.ExpectedVersionIds, test.ExpectedInstalledCount,
        Target = new { test.Request.MinecraftVersion, test.Request.LoaderVersion, test.Request.JavaVersion },
        result.Assurance, result.Findings,
        Artifacts = result.Artifacts.Select(a => new
        {
            ProjectId = a.Project?.Id, VersionId = a.Version?.Id, ProjectTitle = a.Project?.Title,
            FileName = a.File?.FileName ?? Path.GetFileName(a.SourceName), a.SizeBytes,
            a.Sha256, a.Sha512, a.Sha1, a.IsInstalled, a.IsTransitive, a.ProviderVerified,
            ApiVersionUrl = a.Version?.ApiUrl, a.Metadata.IsComplete, a.Metadata.Issues,
            Metadata = a.Metadata.Mods.Select(m => new { m.Id, m.Version, m.Environment, m.NestedDepth,
                m.Provides, m.Depends, m.Recommends, m.Suggests, m.Breaks, m.Conflicts })
        })
    });
    System.Console.Out.WriteLine($"{(passed ? "PASS" : "FAIL")} {test.Name}: resolved={result.IsResolved}; {string.Join(",", codes)}");
    // Persist after every case so interrupted checks cannot erase completed evidence.
    await File.WriteAllTextAsync(args[2], JsonSerializer.Serialize(new { GeneratedAt = DateTimeOffset.UtcNow,
        Scope = "Real API and non-executing JAR declaration checks. No Minecraft startup or user's installed environment tested.",
        Completed = reports.Count, Failures = failures, Cases = reports }, json));
}
return failures == 0 ? 0 : 1;

internal sealed record LiveCase
{
    public string Name { get; init; } = "";
    public ModrinthInspectionRequest Request { get; init; } = new();
    public bool ExpectedResolved { get; init; }
    public IReadOnlyList<string> ExpectedFindingCodes { get; init; } = Array.Empty<string>();
    public IReadOnlyList<ExpectedFinding> ExpectedFindings { get; init; } = Array.Empty<ExpectedFinding>();
    public IReadOnlyList<string>? ExpectedVersionIds { get; init; }
    public int? ExpectedInstalledCount { get; init; }
}

internal sealed record ExpectedFinding(string Code, string? Dependency);
