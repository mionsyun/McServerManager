using MaiPort.Models;
using MaiPort.Utilities;

namespace MaiPort.Services;

/// <summary>
/// ファイアウォール受信規則と UPnP マッピングをまとめて操作し、結果を記録する。
/// </summary>
public sealed class PortControlService : IPortControlService
{
    private readonly IUpnpService _upnp;
    private readonly IFirewallService _firewall;
    private readonly IPortRuleStore _store;
    private readonly SemaphoreSlim _gate = new(1, 1);

    private List<PortRule>? _rules;

    public PortControlService(IUpnpService upnp, IFirewallService firewall, IPortRuleStore store)
    {
        _upnp = upnp;
        _firewall = firewall;
        _store = store;
    }

    public async Task<IReadOnlyList<PortRule>> GetRulesAsync(CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var rules = await EnsureLoadedAsync(ct).ConfigureAwait(false);
            return rules.ToList();
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<PortControlOutcome> OpenAsync(PortOpenRequest request, CancellationToken ct = default)
    {
        ValidateRange(request.Range);
        if (!request.UseUpnp && !request.UseFirewall)
        {
            throw new ArgumentException("UPnP とファイアウォールの少なくとも一方を指定してください。", nameof(request));
        }

        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var rules = await EnsureLoadedAsync(ct).ConfigureAwait(false);
            var messages = new List<string>();
            var description = request.Description?.Trim() ?? string.Empty;
            var rule = Find(rules, request.Range, request.Protocol)
                       ?? new PortRule
                       {
                           Port = request.Range.Start,
                           EndPort = request.Range.End,
                           Protocol = request.Protocol
                       };
            rule.Description = description;

            if (request.UseFirewall)
            {
                var result = await _firewall.AllowAsync(request.Range, request.Protocol, description, ct).ConfigureAwait(false);
                rule.FirewallOpened = result.Success;
                messages.Add(result.Message);
            }

            if (request.UseUpnp)
            {
                var result = await _upnp.OpenAsync(request.Range, request.Protocol, description, ct).ConfigureAwait(false);
                rule.UpnpOpened = result.Success;
                messages.Add(result.Message);
            }

            var success = rule.FirewallOpened || rule.UpnpOpened;
            if (success && !rules.Contains(rule))
            {
                rules.Add(rule);
            }

            // 失敗しても、既存の記録があれば最新の状態に更新しておく。
            if (rules.Contains(rule))
            {
                await _store.SaveAsync(rules, ct).ConfigureAwait(false);
            }

            return new PortControlOutcome(success, messages, rule);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<PortControlOutcome> CloseAsync(PortRange range, PortProtocol protocol, CancellationToken ct = default)
    {
        ValidateRange(range);

        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var rules = await EnsureLoadedAsync(ct).ConfigureAwait(false);
            var messages = new List<string>();

            var firewallResult = await _firewall.RemoveAsync(range, protocol, ct).ConfigureAwait(false);
            messages.Add(firewallResult.Message);

            var upnpResult = await _upnp.CloseAsync(range, protocol, ct).ConfigureAwait(false);
            messages.Add(upnpResult.Message);

            var existing = Find(rules, range, protocol);
            if (existing is not null)
            {
                rules.Remove(existing);
                await _store.SaveAsync(rules, ct).ConfigureAwait(false);
            }

            return new PortControlOutcome(firewallResult.Success || upnpResult.Success, messages, existing);
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task<List<PortRule>> EnsureLoadedAsync(CancellationToken ct)
    {
        return _rules ??= (await _store.LoadAsync(ct).ConfigureAwait(false)).ToList();
    }

    private static PortRule? Find(IEnumerable<PortRule> rules, PortRange range, PortProtocol protocol)
    {
        return rules.FirstOrDefault(rule =>
            rule.Protocol == protocol && ToRange(rule) == range);
    }

    /// <summary>EndPort が未設定（v1.1 以前の記録）なら単一ポートとして扱う。</summary>
    public static PortRange ToRange(PortRule rule)
    {
        return new PortRange(rule.Port, rule.EndPort <= 0 ? rule.Port : rule.EndPort);
    }

    private static void ValidateRange(PortRange range)
    {
        if (!PortRangeParser.IsValid(range))
        {
            throw new ArgumentOutOfRangeException(nameof(range), range, "ポート番号は 1〜65535 で指定してください。");
        }
    }
}
