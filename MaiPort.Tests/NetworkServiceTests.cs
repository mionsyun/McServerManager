using MaiPort.Services;

namespace MaiPort.Tests;

public sealed class NetworkServiceTests
{
    [Theory]
    [InlineData("0.0.0.0:8211", 8211)]
    [InlineData("192.168.1.10:8211", 8211)]
    [InlineData("[::]:8211", 8211)]
    [InlineData("192.168.1.10:18211", 18211)]
    [InlineData("*:*", -1)]
    [InlineData("8211", -1)]
    public void GetPortFromEndPoint_ParsesLocalEndPoint(string endPoint, int expected)
    {
        Assert.Equal(expected, NetworkService.GetPortFromEndPoint(endPoint));
    }
}
