using McServerManager.Services;

namespace McServerManager.Tests;

public sealed class UpnpServiceTests
{
    [Fact]
    public async Task TryOpenPortAsync_WithInvalidPort_ReturnsFailure()
    {
        var service = new UpnpService();

        var (ok, error) = await service.TryOpenPortAsync(-1, "test");

        Assert.False(ok);
        Assert.False(string.IsNullOrWhiteSpace(error));
    }

    [Fact]
    public async Task TryClosePortAsync_DoesNotThrow()
    {
        var service = new UpnpService();

        var ex = await Record.ExceptionAsync(() => service.TryClosePortAsync(-1));

        Assert.Null(ex);
    }
}
