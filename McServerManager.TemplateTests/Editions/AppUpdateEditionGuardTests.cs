using System.Net;
using System.Text;
using McServerManager.Models;
using McServerManager.Models.Editions;
using McServerManager.Services;
using McServerManager.Services.Editions;

namespace McServerManager.TemplateTests.Editions;

public sealed class AppUpdateEditionGuardTests
{
    private const string InstallerUrl = "https://example.invalid/free-installer.exe";
    private const string ValidManifestJson = """
        {"version":"9999.0.0","installerUrl":"https://example.invalid/free-installer.exe",
        "sha256":"aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa"}
        """;

    [Fact]
    public async Task ProDoesNotRequestThePublicFreeManifest()
    {
        using var handler = new RecordingHandler(ValidManifestJson);
        using var client = new HttpClient(handler);
        var service = new AppUpdateService(client, new EditionPolicy(AppEdition.Pro));

        var result = await service.CheckForUpdatesAsync();

        Assert.Equal(AppUpdateCheckStatus.Failed, result.Status);
        Assert.Contains("Pro", result.ErrorMessage);
        Assert.Empty(handler.Requests);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("Free")]
    [InlineData("Pro")]
    [InlineData("Unknown")]
    public async Task ProCannotDownloadOrLaunchEvenWhenCalledDirectly(string? manifestEdition)
    {
        using var handler = new RecordingHandler(ValidManifestJson);
        using var client = new HttpClient(handler);
        var service = new AppUpdateService(client, new EditionPolicy(AppEdition.Pro));
        var manifest = ValidManifest();
        manifest.Edition = manifestEdition;

        var result = await service.DownloadAndLaunchInstallerAsync(manifest);

        Assert.Equal(AppUpdateDownloadStatus.DownloadFailed, result.Status);
        Assert.Contains("Pro", result.ErrorMessage);
        Assert.Null(result.InstallerPath);
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task ProDirectDownloadGuardRunsBeforeManifestValidation()
    {
        using var handler = new RecordingHandler(ValidManifestJson);
        using var client = new HttpClient(handler);
        var service = new AppUpdateService(client, new EditionPolicy(AppEdition.Pro));

        var result = await service.DownloadAndLaunchInstallerAsync(null!);

        Assert.Equal(AppUpdateDownloadStatus.DownloadFailed, result.Status);
        Assert.Contains("Pro", result.ErrorMessage);
        Assert.Empty(handler.Requests);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("\"Free\"")]
    [InlineData("\"free\"")]
    public async Task FreeRetainsLegacyManifestCompatibility(string? editionJson)
    {
        using var handler = new RecordingHandler(WithEdition(editionJson));
        using var client = new HttpClient(handler);
        var service = new AppUpdateService(client, new EditionPolicy(AppEdition.Free));

        var result = await service.CheckForUpdatesAsync();

        Assert.Equal(AppUpdateCheckStatus.UpdateAvailable, result.Status);
        Assert.NotNull(result.Manifest);
        Assert.Equal("9999.0.0", result.Manifest.Version);
        Assert.Equal(InstallerUrl, result.Manifest.InstallerUrl);
        Assert.Equal(editionJson is null ? null : "Free", result.Manifest.Edition);
        Assert.Single(handler.Requests);
        Assert.StartsWith(AppUpdateService.ManifestUrl + "?", handler.Requests[0].AbsoluteUri);
    }

    [Theory]
    [InlineData("\"Pro\"")]
    [InlineData("\"Enterprise\"")]
    [InlineData("\"\"")]
    [InlineData("\" Free \"")]
    [InlineData("null")]
    [InlineData("0")]
    [InlineData("false")]
    [InlineData("{}")]
    [InlineData("[]")]
    public async Task SuppliedWrongEditionMetadataFailsClosed(string editionJson)
    {
        using var handler = new RecordingHandler(WithEdition(editionJson));
        using var client = new HttpClient(handler);
        var service = new AppUpdateService(client, new EditionPolicy(AppEdition.Free));

        var result = await service.CheckForUpdatesAsync();

        Assert.Equal(AppUpdateCheckStatus.Failed, result.Status);
        Assert.Null(result.Manifest);
        Assert.Contains("edition", result.ErrorMessage);
        Assert.Single(handler.Requests);
    }

    [Theory]
    [InlineData("\"edition\":\"Pro\",\"edition\":\"Free\"")]
    [InlineData("\"Edition\":\"Free\",\"edition\":\"Pro\"")]
    [InlineData("\"edition\":\"Free\",\"edition\":\"Free\"")]
    [InlineData("\"\\u0065dition\":\"Free\",\"EDITION\":\"Free\"")]
    public async Task DuplicateOrCaseAliasedEditionMetadataFailsClosed(string metadata)
    {
        var json = ValidManifestJson.TrimEnd()[..^1] + "," + metadata + "}";
        using var handler = new RecordingHandler(json);
        using var client = new HttpClient(handler);
        var service = new AppUpdateService(client, new EditionPolicy(AppEdition.Free));

        var result = await service.CheckForUpdatesAsync();

        Assert.Equal(AppUpdateCheckStatus.Failed, result.Status);
        Assert.Null(result.Manifest);
        Assert.Single(handler.Requests);
    }

    [Theory]
    [InlineData("Pro")]
    [InlineData("Unknown")]
    [InlineData("")]
    public async Task FreeDirectDownloadRejectsWrongEditionBeforeNetwork(string edition)
    {
        using var handler = new RecordingHandler(ValidManifestJson);
        using var client = new HttpClient(handler);
        var service = new AppUpdateService(client, new EditionPolicy(AppEdition.Free));
        var manifest = ValidManifest();
        manifest.Edition = edition;

        var result = await service.DownloadAndLaunchInstallerAsync(manifest);

        Assert.Equal(AppUpdateDownloadStatus.DownloadFailed, result.Status);
        Assert.Contains("edition", result.ErrorMessage);
        Assert.Null(result.InstallerPath);
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task DefaultUpdaterUsesTheBuildIdentity()
    {
        using var handler = new RecordingHandler(ValidManifestJson);
        using var client = new HttpClient(handler);
        var service = new AppUpdateService(client);

        var result = await service.CheckForUpdatesAsync();

#if MAIPILOT_PRO
        Assert.Equal(AppUpdateCheckStatus.Failed, result.Status);
        Assert.Empty(handler.Requests);
#else
        Assert.Equal(AppUpdateCheckStatus.UpdateAvailable, result.Status);
        Assert.Single(handler.Requests);
#endif
    }

    private static AppUpdateManifest ValidManifest() => new()
    {
        Version = "9999.0.0",
        InstallerUrl = InstallerUrl,
        Sha256 = new string('a', 64)
    };

    private static string WithEdition(string? value) => value is null
        ? ValidManifestJson
        : ValidManifestJson.TrimEnd()[..^1] + ",\"edition\":" + value + "}";

    // This handler never accesses the network and never supplies an executable body.
    private sealed class RecordingHandler(string json) : HttpMessageHandler
    {
        public List<Uri> Requests { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(request.RequestUri!);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json")
            });
        }
    }
}
