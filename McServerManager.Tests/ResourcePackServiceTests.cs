using System.Net;
using System.Net.Sockets;
using McServerManager.Services;
using McServerManager.Tests.TestInfrastructure;

namespace McServerManager.Tests;

public sealed class ResourcePackServiceTests
{
    [Fact]
    public void ComputeSha1_ReturnsExpectedHash()
    {
        using var temp = new TemporaryDirectoryScope();
        var filePath = Path.Combine(temp.Path, "pack.zip");
        File.WriteAllBytes(filePath, [1, 2, 3]);
        var service = new ResourcePackService();

        var sha1 = service.ComputeSha1(filePath);

        Assert.Equal("7037807198c22a7d2b0807371d763779a84fdfcf", sha1);
    }

    [Fact]
    public async Task StartAsync_ServesFile_AndStopShutsDown()
    {
        using var temp = new TemporaryDirectoryScope();
        var filePath = Path.Combine(temp.Path, "pack.zip");
        var payload = new byte[] { 11, 22, 33, 44, 55 };
        File.WriteAllBytes(filePath, payload);
        var port = GetFreeTcpPort();
        var service = new ResourcePackService();

        await service.StartAsync(filePath, port);
        await AsyncTestWait.WaitUntilAsync(() => service.IsRunning, TimeSpan.FromSeconds(2));

        using var http = new HttpClient();
        var response = await http.GetAsync($"http://127.0.0.1:{port}/");
        var received = await response.Content.ReadAsByteArrayAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(payload, received);

        await service.StopAsync();
        Assert.False(service.IsRunning);
    }

    [Fact]
    public async Task StartAsync_WhenFileMissing_Returns404()
    {
        using var temp = new TemporaryDirectoryScope();
        var filePath = Path.Combine(temp.Path, "missing.zip");
        var port = GetFreeTcpPort();
        var service = new ResourcePackService();

        await service.StartAsync(filePath, port);
        await AsyncTestWait.WaitUntilAsync(() => service.IsRunning, TimeSpan.FromSeconds(2));

        using var http = new HttpClient();
        var response = await http.GetAsync($"http://127.0.0.1:{port}/");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);

        await service.StopAsync();
    }

    private static int GetFreeTcpPort()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        return ((IPEndPoint)listener.LocalEndpoint).Port;
    }
}
