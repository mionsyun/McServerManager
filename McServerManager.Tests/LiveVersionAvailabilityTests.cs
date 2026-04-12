using System.Net.Http;
using System.Text.Json;
using System.Xml.Linq;
using McServerManager.Tests.TestInfrastructure;

namespace McServerManager.Tests;

public sealed class LiveVersionAvailabilityTests
{
    private const string MinecraftVersionManifest = "https://launchermeta.mojang.com/mc/game/version_manifest.json";
    private const string PaperApiBase = "https://api.papermc.io/v2/projects";
    private const string PurpurApiBase = "https://api.purpurmc.org/v2/purpur";
    private const string FabricApiBase = "https://meta.fabricmc.net/v2/versions";
    private const string ForgeMavenMetadata = "https://maven.minecraftforge.net/net/minecraftforge/forge/maven-metadata.xml";
    private const string SpigotDirectJarTemplate = "https://download.getbukkit.org/spigot/spigot-{0}.jar";
    private const string SpigotDirectJarFallbackTemplate = "https://cdn.getbukkit.org/spigot/spigot-{0}.jar";

    [SkippableTheory]
    [InlineData("Vanilla", "1.16.5")]
    [InlineData("Vanilla", "1.17.1")]
    [InlineData("Vanilla", "1.18.2")]
    [InlineData("Vanilla", "1.19.4")]
    [InlineData("Vanilla", "1.20.1")]
    [InlineData("Vanilla", "1.20.6")]
    [InlineData("Vanilla", "1.21.1")]
    [InlineData("Paper", "1.16.5")]
    [InlineData("Paper", "1.17.1")]
    [InlineData("Paper", "1.18.2")]
    [InlineData("Paper", "1.19.4")]
    [InlineData("Paper", "1.20.1")]
    [InlineData("Paper", "1.20.6")]
    [InlineData("Paper", "1.21.1")]
    [InlineData("Purpur", "1.16.5")]
    [InlineData("Purpur", "1.17.1")]
    [InlineData("Purpur", "1.18.2")]
    [InlineData("Purpur", "1.19.4")]
    [InlineData("Purpur", "1.20.1")]
    [InlineData("Purpur", "1.20.6")]
    [InlineData("Purpur", "1.21.1")]
    [InlineData("Fabric", "1.16.5")]
    [InlineData("Fabric", "1.17.1")]
    [InlineData("Fabric", "1.18.2")]
    [InlineData("Fabric", "1.19.4")]
    [InlineData("Fabric", "1.20.1")]
    [InlineData("Fabric", "1.20.6")]
    [InlineData("Fabric", "1.21.1")]
    [InlineData("Forge", "1.16.5")]
    [InlineData("Forge", "1.17.1")]
    [InlineData("Forge", "1.18.2")]
    [InlineData("Forge", "1.19.4")]
    [InlineData("Forge", "1.20.1")]
    [InlineData("Forge", "1.20.6")]
    [InlineData("Forge", "1.21.1")]
    [InlineData("Spigot", "1.16.5")]
    [InlineData("Spigot", "1.18.2")]
    [InlineData("Spigot", "1.19.4")]
    [InlineData("Spigot", "1.20.1")]
    [InlineData("Spigot", "1.20.6")]
    [InlineData("Spigot", "1.21.1")]
    public async Task UpstreamVersionAvailability_Matrix(string serverType, string version)
    {
        Skip.IfNot(
            LiveTestGate.IsLiveEnabled(),
            $"Set {LiveTestGate.LiveEnvVar}=1 to run live availability checks.");

        if (string.Equals(serverType, "Spigot", StringComparison.OrdinalIgnoreCase))
        {
            Skip.IfNot(
                LiveTestGate.CanResolveAnyHost("download.getbukkit.org", "cdn.getbukkit.org"),
                "Neither download.getbukkit.org nor cdn.getbukkit.org is resolvable in this environment.");
        }

        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
        http.DefaultRequestHeaders.UserAgent.ParseAdd("McServerManager.Tests/1.0");

        switch (serverType)
        {
            case "Vanilla":
                await AssertVanillaAsync(http, version);
                break;
            case "Paper":
                await AssertPaperAsync(http, version);
                break;
            case "Purpur":
                await AssertPurpurAsync(http, version);
                break;
            case "Fabric":
                await AssertFabricAsync(http, version);
                break;
            case "Forge":
                await AssertForgeAsync(http, version);
                break;
            case "Spigot":
                await AssertSpigotAsync(http, version);
                break;
            default:
                throw new InvalidOperationException($"Unsupported server type: {serverType}");
        }
    }

    private static async Task AssertVanillaAsync(HttpClient http, string version)
    {
        var manifestJson = await http.GetStringAsync(MinecraftVersionManifest);
        using var doc = JsonDocument.Parse(manifestJson);
        var versions = doc.RootElement.GetProperty("versions").EnumerateArray();
        var found = versions.Any(element =>
            string.Equals(element.GetProperty("id").GetString(), version, StringComparison.OrdinalIgnoreCase));

        Assert.True(found, $"Vanilla version was not found: {version}");
    }

    private static async Task AssertPaperAsync(HttpClient http, string version)
    {
        var url = $"{PaperApiBase}/paper/versions/{version}";
        var json = await http.GetStringAsync(url);
        using var doc = JsonDocument.Parse(json);
        var builds = doc.RootElement.GetProperty("builds");
        Assert.True(builds.GetArrayLength() > 0, $"Paper builds were not found for version {version}");
    }

    private static async Task AssertPurpurAsync(HttpClient http, string version)
    {
        var url = $"{PurpurApiBase}/{version}";
        var json = await http.GetStringAsync(url);
        using var doc = JsonDocument.Parse(json);
        var allBuilds = doc.RootElement.GetProperty("builds").GetProperty("all");
        Assert.True(allBuilds.GetArrayLength() > 0, $"Purpur builds were not found for version {version}");
    }

    private static async Task AssertFabricAsync(HttpClient http, string version)
    {
        var url = $"{FabricApiBase}/loader/{version}";
        var json = await http.GetStringAsync(url);
        using var doc = JsonDocument.Parse(json);
        Assert.True(doc.RootElement.GetArrayLength() > 0, $"Fabric loader versions were not found for version {version}");
    }

    private static async Task AssertForgeAsync(HttpClient http, string version)
    {
        var xml = await http.GetStringAsync(ForgeMavenMetadata);
        var document = XDocument.Parse(xml);
        var forgeVersions = document.Descendants("version").Select(element => element.Value);
        var found = forgeVersions.Any(value => value.StartsWith($"{version}-", StringComparison.OrdinalIgnoreCase));
        Assert.True(found, $"Forge version metadata was not found for version {version}");
    }

    private static async Task AssertSpigotAsync(HttpClient http, string version)
    {
        var urls = new[]
        {
            string.Format(SpigotDirectJarTemplate, version),
            string.Format(SpigotDirectJarFallbackTemplate, version)
        };

        var errors = new List<string>();
        foreach (var url in urls)
        {
            try
            {
                using var response = await http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead);
                if (response.IsSuccessStatusCode)
                {
                    return;
                }

                errors.Add($"{url} => {(int)response.StatusCode}");
            }
            catch (HttpRequestException ex)
            {
                errors.Add($"{url} => {ex.Message}");
            }
        }

        Assert.Fail($"Spigot direct download endpoint failed for version {version}: {string.Join(" | ", errors)}");
    }
}
