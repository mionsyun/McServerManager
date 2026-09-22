using MaiPort.Models;
using MaiPort.Services;

namespace MaiPort.Tests;

public sealed class BedrockServerServiceTests
{
    private readonly BedrockServerService _service = new();

    private static IReadOnlyDictionary<string, string> Properties(params string[] lines)
        => BedrockServerService.ParseProperties(lines);

    [Fact]
    public void BuildPlan_DefaultTransport_IsNetherNetWithTcpSignaling()
    {
        // transport 未指定のときの既定は nethernet。
        var plan = _service.BuildPlan(Properties("server-port=19132"));

        Assert.Equal("nethernet", plan.Transport);
        var item = Assert.Single(plan.Items);
        Assert.Equal(PortProtocol.Tcp, item.Protocol);
        Assert.Equal(new PortRange(19132, 19132), item.Range);
        Assert.Contains(plan.Notes, note => note.Contains("server-udp-ports"));
    }

    [Fact]
    public void BuildPlan_NetherNetWithUdpRange_AddsUdpPorts()
    {
        var plan = _service.BuildPlan(Properties(
            "transport=nethernet",
            "server-port=19132",
            "server-udp-ports=49152-49200"));

        Assert.Equal(2, plan.Items.Count);
        Assert.Contains(plan.Items, item => item.Protocol == PortProtocol.Tcp && item.Range == new PortRange(19132, 19132));
        Assert.Contains(plan.Items, item => item.Protocol == PortProtocol.Udp && item.Range == new PortRange(49152, 49200));
    }

    [Fact]
    public void BuildPlan_RakNet_UsesUdpPortsOnly()
    {
        var plan = _service.BuildPlan(Properties(
            "transport=raknet",
            "server-port=19132",
            "server-portv6=19133"));

        Assert.Equal("raknet", plan.Transport);
        Assert.Equal(2, plan.Items.Count);
        Assert.All(plan.Items, item => Assert.Equal(PortProtocol.Udp, item.Protocol));
    }

    [Fact]
    public void BuildPlan_RakNetWithCustomPortAndLanVisibility_NotesDefaultPorts()
    {
        var plan = _service.BuildPlan(Properties(
            "transport=raknet",
            "server-port=25000",
            "enable-lan-visibility=true"));

        Assert.Contains(plan.Notes, note => note.Contains("19132"));
    }

    [Theory]
    // 内部ポートのみ / 範囲 / 外部:内部 / IP付き / IPv6リテラル付き
    [InlineData("49152", 49152, 49152)]
    [InlineData("49152-49200", 49152, 49200)]
    [InlineData("19132-19232:32000-32100", 32000, 32100)]
    [InlineData("203.0.113.10:19132-19232:32000-32100", 32000, 32100)]
    [InlineData("[2001:db8::1]:19132-19232:32000-32100", 32000, 32100)]
    public void ParseUdpPorts_ReturnsInternalPorts(string value, int start, int end)
    {
        var ranges = BedrockServerService.ParseUdpPorts([value]);

        var range = Assert.Single(ranges);
        Assert.Equal(new PortRange(start, end), range);
    }

    [Fact]
    public void ParseProperties_MultipleUdpPortLines_AreCombined()
    {
        var plan = _service.BuildPlan(Properties(
            "# コメント行",
            "transport=nethernet",
            "server-udp-ports=49152-49160",
            "server-udp-ports=50000"));

        var udpItems = plan.Items.Where(item => item.Protocol == PortProtocol.Udp).ToList();
        Assert.Equal(2, udpItems.Count);
        Assert.Contains(udpItems, item => item.Range == new PortRange(49152, 49160));
        Assert.Contains(udpItems, item => item.Range == new PortRange(50000, 50000));
    }
}
