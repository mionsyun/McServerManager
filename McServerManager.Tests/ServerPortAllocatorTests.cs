using McServerManager.Services;

namespace McServerManager.Tests;

public sealed class ServerPortAllocatorTests
{
    [Fact]
    public void FindFreePort_SkipsPortsUsedByOtherServers()
    {
        var used = new HashSet<int> { 25565, 25566, 25567 };

        Assert.Equal(25568, ServerPortAllocator.FindFreePort(25566, used));
    }

    [Fact]
    public void FindFreePort_ForBedrockPair_DoesNotCollideWithNewIpv4Port()
    {
        var used = new HashSet<int> { 19132, 19133 };

        var port = ServerPortAllocator.FindFreePort(19133, used);
        used.Add(port);
        var portV6 = ServerPortAllocator.FindFreePort(19134, used);

        Assert.Equal(19134, port);
        Assert.Equal(19135, portV6);
    }

    [Fact]
    public void FindFreePort_WhenTopIsExhausted_WrapsAround()
    {
        var used = new HashSet<int> { 65535 };

        Assert.Equal(1024, ServerPortAllocator.FindFreePort(65535, used));
    }
}
