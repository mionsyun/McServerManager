using System.Net;
using McServerManager.Models;
using McServerManager.Services;
using McServerManager.Tests.TestInfrastructure;

namespace McServerManager.Tests;

public sealed class AppUpdateServiceTests
{
    [Fact]
    public async Task CheckForUpdatesAsync_WhenManifestIsNewer_ReturnsUpdateAvailable()
    {
        var handler = new StubHttpMessageHandler(request =>
        {
            if (request.RequestUri?.AbsoluteUri.StartsWith(AppUpdateService.ManifestUrl, StringComparison.OrdinalIgnoreCase) == true)
            {
                return StubHttpMessageHandler.Json(
                    """
                    {
                      "version": "9999.0.0",
                      "installerUrl": "https://example.com/installer.exe",
                      "sha256": "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa"
                    }
                    """);
            }

            return new HttpResponseMessage(HttpStatusCode.NotFound);
        });
        using var client = new HttpClient(handler);
        var service = new AppUpdateService(client);

        var result = await service.CheckForUpdatesAsync();

        Assert.Equal(AppUpdateCheckStatus.UpdateAvailable, result.Status);
        Assert.NotNull(result.Manifest);
        Assert.Equal("9999.0.0", result.Manifest!.Version);
    }

    [Fact]
    public async Task CheckForUpdatesAsync_WhenManifestIsInvalid_ReturnsFailed()
    {
        var handler = new StubHttpMessageHandler(request =>
        {
            if (request.RequestUri?.AbsoluteUri.StartsWith(AppUpdateService.ManifestUrl, StringComparison.OrdinalIgnoreCase) == true)
            {
                return StubHttpMessageHandler.Json(
                    """
                    {
                      "version": "1.2.3",
                      "installerUrl": "https://example.com/installer.exe",
                      "sha256": "short"
                    }
                    """);
            }

            return new HttpResponseMessage(HttpStatusCode.NotFound);
        });
        using var client = new HttpClient(handler);
        var service = new AppUpdateService(client);

        var result = await service.CheckForUpdatesAsync();

        Assert.Equal(AppUpdateCheckStatus.Failed, result.Status);
        Assert.NotNull(result.ErrorMessage);
    }

    [Fact]
    public async Task DownloadAndLaunchInstallerAsync_WhenHashMismatches_ReturnsHashMismatch()
    {
        using var appData = new TemporaryAppDataScope();

        var handler = new StubHttpMessageHandler(request =>
        {
            if (request.RequestUri?.AbsoluteUri == "https://example.com/installer.exe")
            {
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new ByteArrayContent([1, 2, 3, 4, 5])
                };
            }

            return new HttpResponseMessage(HttpStatusCode.NotFound);
        });
        using var client = new HttpClient(handler);
        var service = new AppUpdateService(client);
        var manifest = new AppUpdateManifest
        {
            Version = "9.9.9",
            InstallerUrl = "https://example.com/installer.exe",
            Sha256 = "ffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffff"
        };

        var result = await service.DownloadAndLaunchInstallerAsync(manifest);

        Assert.Equal(AppUpdateDownloadStatus.HashMismatch, result.Status);
        Assert.NotNull(result.ErrorMessage);
    }
}
