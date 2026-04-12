using McServerManager.Services;

namespace McServerManager.Tests;

public sealed class FirewallServiceTests
{
    [Fact]
    public void BuildRuleInfo_SanitizesServerId()
    {
        var service = new FirewallService();

        var info = service.BuildRuleInfo("srv \"../; drop-all");

        Assert.Matches("^McServerManager_[A-Za-z0-9_\\-.]+_TCP$", info.TcpRuleName);
        Assert.Matches("^McServerManager_[A-Za-z0-9_\\-.]+_UDP$", info.UdpRuleName);
        Assert.DoesNotContain("\"", info.TcpRuleName, StringComparison.Ordinal);
        Assert.DoesNotContain(" ", info.TcpRuleName, StringComparison.Ordinal);
        Assert.DoesNotContain(";", info.TcpRuleName, StringComparison.Ordinal);
    }
}
