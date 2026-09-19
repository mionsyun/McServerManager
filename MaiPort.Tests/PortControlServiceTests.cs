using MaiPort.Models;
using MaiPort.Services;

namespace MaiPort.Tests;

public sealed class PortControlServiceTests
{
    [Fact]
    public async Task OpenAsync_UdpPort_RecordsRule()
    {
        var upnp = new FakeUpnpService();
        var firewall = new FakeFirewallService();
        var store = new FakePortRuleStore();
        var service = new PortControlService(upnp, firewall, store);

        var outcome = await service.OpenAsync(new PortOpenRequest(8211, PortProtocol.Udp, "Palworld", UseUpnp: true, UseFirewall: true));

        Assert.True(outcome.Success);
        Assert.Contains((8211, PortProtocol.Udp), upnp.Opened);
        Assert.Contains((8211, PortProtocol.Udp), firewall.Allowed);

        var rules = await service.GetRulesAsync();
        var rule = Assert.Single(rules);
        Assert.Equal(8211, rule.Port);
        Assert.Equal(PortProtocol.Udp, rule.Protocol);
        Assert.Equal("Palworld", rule.Description);
        Assert.True(rule.UpnpOpened);
        Assert.True(rule.FirewallOpened);
        Assert.Equal(1, store.SaveCount);
    }

    [Fact]
    public async Task OpenAsync_FirewallOnly_DoesNotCallUpnp()
    {
        var upnp = new FakeUpnpService();
        var firewall = new FakeFirewallService();
        var service = new PortControlService(upnp, firewall, new FakePortRuleStore());

        var outcome = await service.OpenAsync(new PortOpenRequest(8211, PortProtocol.Udp, "Palworld", UseUpnp: false, UseFirewall: true));

        Assert.True(outcome.Success);
        Assert.Empty(upnp.Opened);
        Assert.False(outcome.Rule!.UpnpOpened);
    }

    [Fact]
    public async Task OpenAsync_AllOperationsFail_DoesNotRecordRule()
    {
        var upnp = new FakeUpnpService { NextResultSucceeds = false };
        var firewall = new FakeFirewallService { NextResultSucceeds = false };
        var store = new FakePortRuleStore();
        var service = new PortControlService(upnp, firewall, store);

        var outcome = await service.OpenAsync(new PortOpenRequest(8211, PortProtocol.Udp, "Palworld", UseUpnp: true, UseFirewall: true));

        Assert.False(outcome.Success);
        Assert.Empty(await service.GetRulesAsync());
        Assert.Equal(0, store.SaveCount);
    }

    [Fact]
    public async Task OpenAsync_SamePortTwice_KeepsSingleRule()
    {
        var service = new PortControlService(new FakeUpnpService(), new FakeFirewallService(), new FakePortRuleStore());

        await service.OpenAsync(new PortOpenRequest(8211, PortProtocol.Udp, "Palworld", UseUpnp: true, UseFirewall: true));
        await service.OpenAsync(new PortOpenRequest(8211, PortProtocol.Udp, "Palworld 2", UseUpnp: true, UseFirewall: true));

        var rule = Assert.Single(await service.GetRulesAsync());
        Assert.Equal("Palworld 2", rule.Description);
    }

    [Fact]
    public async Task CloseAsync_RemovesRecordedRule()
    {
        var upnp = new FakeUpnpService();
        var firewall = new FakeFirewallService();
        var service = new PortControlService(upnp, firewall, new FakePortRuleStore());
        await service.OpenAsync(new PortOpenRequest(8211, PortProtocol.Udp, "Palworld", UseUpnp: true, UseFirewall: true));

        var outcome = await service.CloseAsync(8211, PortProtocol.Udp);

        Assert.True(outcome.Success);
        Assert.Contains((8211, PortProtocol.Udp), upnp.Closed);
        Assert.Contains((8211, PortProtocol.Udp), firewall.Removed);
        Assert.Empty(await service.GetRulesAsync());
    }

    [Theory]
    [InlineData(0)]
    [InlineData(65536)]
    public async Task OpenAsync_InvalidPort_Throws(int port)
    {
        var service = new PortControlService(new FakeUpnpService(), new FakeFirewallService(), new FakePortRuleStore());

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(
            () => service.OpenAsync(new PortOpenRequest(port, PortProtocol.Udp, "test", UseUpnp: true, UseFirewall: true)));
    }

    [Fact]
    public async Task OpenAsync_NoTargetSelected_Throws()
    {
        var service = new PortControlService(new FakeUpnpService(), new FakeFirewallService(), new FakePortRuleStore());

        await Assert.ThrowsAsync<ArgumentException>(
            () => service.OpenAsync(new PortOpenRequest(8211, PortProtocol.Udp, "test", UseUpnp: false, UseFirewall: false)));
    }
}
