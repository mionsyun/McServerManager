using MaiPort.Models;
using MaiPort.Services;

namespace MaiPort.Tests;

internal sealed class FakeUpnpService : IUpnpService
{
    public bool NextResultSucceeds { get; set; } = true;

    public List<(int Port, PortProtocol Protocol)> Opened { get; } = [];

    public List<(int Port, PortProtocol Protocol)> Closed { get; } = [];

    public Task<PortOperationResult> OpenAsync(int port, PortProtocol protocol, string description, CancellationToken ct = default)
    {
        if (NextResultSucceeds)
        {
            Opened.Add((port, protocol));
        }

        return Task.FromResult(NextResultSucceeds
            ? PortOperationResult.Ok("upnp ok")
            : PortOperationResult.Failed("upnp ng"));
    }

    public Task<PortOperationResult> CloseAsync(int port, PortProtocol protocol, CancellationToken ct = default)
    {
        Closed.Add((port, protocol));
        return Task.FromResult(PortOperationResult.Ok("upnp closed"));
    }

    public Task<bool> IsDeviceAvailableAsync(CancellationToken ct = default) => Task.FromResult(true);

    public Task<string?> GetExternalIpAsync(CancellationToken ct = default) => Task.FromResult<string?>("203.0.113.1");
}

internal sealed class FakeFirewallService : IFirewallService
{
    public bool NextResultSucceeds { get; set; } = true;

    public List<(int Port, PortProtocol Protocol)> Allowed { get; } = [];

    public List<(int Port, PortProtocol Protocol)> Removed { get; } = [];

    public bool IsAdministrator() => true;

    public string BuildRuleName(int port, PortProtocol protocol) => $"MaiPort_{protocol}_{port}";

    public Task<PortOperationResult> AllowAsync(int port, PortProtocol protocol, string description, CancellationToken ct = default)
    {
        if (NextResultSucceeds)
        {
            Allowed.Add((port, protocol));
        }

        return Task.FromResult(NextResultSucceeds
            ? PortOperationResult.Ok("firewall ok")
            : PortOperationResult.Failed("firewall ng"));
    }

    public Task<PortOperationResult> RemoveAsync(int port, PortProtocol protocol, CancellationToken ct = default)
    {
        Removed.Add((port, protocol));
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
