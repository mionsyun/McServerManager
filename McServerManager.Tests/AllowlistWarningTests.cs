using McServerManager.Models;
using McServerManager.Services;
using McServerManager.Tests.TestInfrastructure;

namespace McServerManager.Tests;

public sealed class AllowlistWarningTests
{
    [Theory]
    [InlineData(true, 0, true)]
    [InlineData(true, 1, false)]
    [InlineData(false, 0, false)]
    public void GetPlan_WarnsOnlyWhenAllowlistIsOnAndEmpty(bool allowList, int entries, bool expectWarning)
    {
        using var temp = new TemporaryDirectoryScope();
        var properties = new BedrockPropertiesService();
        File.WriteAllText(Path.Combine(temp.Path, ServerEditions.BedrockExecutableName), string.Empty);
        properties.Save(temp.Path, new BedrockServerProperties { AllowList = allowList, Transport = "raknet" });
        properties.SaveAllowlist(temp.Path, Enumerable.Range(0, entries).Select(i => new BedrockAllowlistEntry { Name = $"Player{i}" }));
        var config = new ServerConfig
        {
            ServerId = "allowlist-warning",
            Type = ServerEditions.BedrockType,
            Version = "1.26.52.3",
            DirectoryPath = temp.Path,
            Port = 19132
        };

        var plan = new PortForwardingService(new StubUpnpService(), properties).GetPlan(config);

        Assert.Equal(expectWarning, plan.Warnings.Contains(PortForwardingService.AllowlistEmptyWarning));
    }
}
