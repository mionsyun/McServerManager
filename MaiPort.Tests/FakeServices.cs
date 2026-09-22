using MaiPort.Models;
using MaiPort.Utilities;
using MaiPort.Services;

namespace MaiPort.Tests;

internal sealed class FakeUpnpService : IUpnpService
{
    public bool NextResultSucceeds { get; set; } = true;

    public List<(PortRange Range, PortProtocol Protocol)> Opened { get; } = [];

    public List<(PortRange Range, PortProtocol Protocol)> Closed { get; } = [];

    public Task<PortOperationResult> OpenAsync(PortRange range, PortProtocol protocol, string description, CancellationToken ct = default)
    {
        if (NextResultSucceeds)
        {
            Opened.Add((range, protocol));
        }

        return Task.FromResult(NextResultSucceeds
            ? PortOperationResult.Ok("upnp ok")
            : PortOperationResult.Failed("upnp ng"));
    }

    public Task<PortOperationResult> CloseAsync(PortRange range, PortProtocol protocol, CancellationToken ct = default)
    {
        Closed.Add((range, protocol));
        return Task.FromResult(PortOperationResult.Ok("upnp closed"));
    }

    public Task<bool> IsDeviceAvailableAsync(CancellationToken ct = default) => Task.FromResult(true);

    public Task<string?> GetExternalIpAsync(CancellationToken ct = default) => Task.FromResult<string?>("203.0.113.1");
}

internal sealed class FakeFirewallService : IFirewallService
{
    public bool NextResultSucceeds { get; set; } = true;

    public List<(PortRange Range, PortProtocol Protocol)> Allowed { get; } = [];

    public List<(PortRange Range, PortProtocol Protocol)> Removed { get; } = [];

    public bool IsAdministrator() => true;

    public string BuildRuleName(PortRange range, PortProtocol protocol) => $"MaiPort_{protocol}_{PortRangeParser.Format(range)}";

    public Task<PortOperationResult> AllowAsync(PortRange range, PortProtocol protocol, string description, CancellationToken ct = default)
    {
        if (NextResultSucceeds)
        {
            Allowed.Add((range, protocol));
        }

        return Task.FromResult(NextResultSucceeds
            ? PortOperationResult.Ok("firewall ok")
            : PortOperationResult.Failed("firewall ng"));
    }

    public Task<PortOperationResult> RemoveAsync(PortRange range, PortProtocol protocol, CancellationToken ct = default)
    {
        Removed.Add((range, protocol));
        return Task.FromResult(PortOperationResult.Ok("firewall removed"));
    }
}

internal sealed class FakePortRuleStore : IPortRuleStore
{
    public List<PortRule> Saved { get; private set; } = [];

    public int SaveCount { get; private set; }

    public Task<IReadOnlyList<PortRule>> LoadAsync(CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<PortRule>>(Saved.ToList());

    public Task SaveAsync(IReadOnlyList<PortRule> rules, CancellationToken ct = default)
    {
        Saved = rules.ToList();
        SaveCount++;
        return Task.CompletedTask;
    }
}
