using MaiPort.Models;
using MaiPort.Services;

namespace MaiPort.Tests;

public sealed class PortRuleStoreTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "MaiPortTests_" + Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task LoadAsync_WhenFileMissing_ReturnsEmpty()
    {
        var store = new PortRuleStore(_directory);

        Assert.Empty(await store.LoadAsync());
    }

    [Fact]
    public async Task SaveAsync_ThenLoadAsync_RoundTripsRules()
    {
        var store = new PortRuleStore(_directory);
        var rule = new PortRule
        {
            Port = 8211,
            Protocol = PortProtocol.Udp,
            Description = "Palworld",
            UpnpOpened = true,
            FirewallOpened = true
        };

        await store.SaveAsync([rule]);
        var loaded = await store.LoadAsync();

        var actual = Assert.Single(loaded);
        Assert.Equal(8211, actual.Port);
        Assert.Equal(PortProtocol.Udp, actual.Protocol);
        Assert.Equal("Palworld", actual.Description);
        Assert.True(actual.UpnpOpened);
        Assert.True(actual.FirewallOpened);
    }

    [Fact]
    public async Task LoadAsync_WhenFileIsBroken_ReturnsEmpty()
    {
        Directory.CreateDirectory(_directory);
        await File.WriteAllTextAsync(Path.Combine(_directory, "rules.json"), "{ broken json");

        Assert.Empty(await new PortRuleStore(_directory).LoadAsync());
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }
}
