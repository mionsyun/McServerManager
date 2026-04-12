using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using McServerManager.Services;
using McServerManager.Tests.TestInfrastructure;

namespace McServerManager.Tests;

public sealed class NetworkServiceTests
{
    [Fact]
    public void GetExternalChecklist_ReturnsExpectedItems()
    {
        var service = new NetworkService();

        var checklist = service.GetExternalChecklist();

        Assert.Equal(4, checklist.Count);
        Assert.Contains(checklist, item => item.Contains("ポート", StringComparison.Ordinal));
        Assert.Contains(checklist, item => item.Contains("Firewall", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task GetProcessesUsingPort_WhenListenerExists_FindsCurrentProcess()
    {
        var service = new NetworkService();
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;

        await AsyncTestWait.WaitUntilAsync(
            () => service.GetProcessesUsingPort(port).Count > 0,
            TimeSpan.FromSeconds(5),
            TimeSpan.FromMilliseconds(150));

        var processes = service.GetProcessesUsingPort(port);

        Assert.Contains(processes, process => process.Id == Process.GetCurrentProcess().Id);
    }

    [Fact]
    public void TryKillProcessesUsingPort_WhenNoProcessIsListening_ReturnsTrue()
    {
        var port = GetUnusedPort();
        var service = new NetworkService();

        var ok = service.TryKillProcessesUsingPort(port, out var error);

        Assert.True(ok);
        Assert.Null(error);
    }

    private static int GetUnusedPort()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }
}
