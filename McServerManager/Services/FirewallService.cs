using System.Diagnostics;
using System.Security.Principal;
using System.Text.RegularExpressions;
using McServerManager.Models;

namespace McServerManager.Services;

public sealed class FirewallService : IFirewallService
{
    private static readonly Regex UnsafeRuleNameChars = new(@"[^A-Za-z0-9_\-\.]", RegexOptions.Compiled);

    public bool IsAdministrator()
    {
        using var identity = WindowsIdentity.GetCurrent();
        var principal = new WindowsPrincipal(identity);
        return principal.IsInRole(WindowsBuiltInRole.Administrator);
    }

    public FirewallRuleInfo BuildRuleInfo(string serverId)
    {
        var sanitizedServerId = NormalizeRuleName(serverId, "server");
        return new FirewallRuleInfo
        {
            TcpRuleName = $"McServerManager_{sanitizedServerId}_TCP",
            UdpRuleName = $"McServerManager_{sanitizedServerId}_UDP"
        };
    }

    public void CreateRules(int port, FirewallRuleInfo info)
    {
        ValidatePort(port);
        var tcpRuleName = NormalizeRuleName(info.TcpRuleName, "McServerManager_TCP");
        var udpRuleName = NormalizeRuleName(info.UdpRuleName, "McServerManager_UDP");

        RunNetsh(["advfirewall", "firewall", "add", "rule", $"name={tcpRuleName}", "dir=in", "action=allow", "protocol=TCP", $"localport={port}"]);
        RunNetsh(["advfirewall", "firewall", "add", "rule", $"name={udpRuleName}", "dir=in", "action=allow", "protocol=UDP", $"localport={port}"]);
    }

    public void DeleteRules(FirewallRuleInfo info)
    {
        if (!string.IsNullOrWhiteSpace(info.TcpRuleName))
        {
            var tcpRuleName = NormalizeRuleName(info.TcpRuleName, "McServerManager_TCP");
            RunNetsh(["advfirewall", "firewall", "delete", "rule", $"name={tcpRuleName}"]);
        }

        if (!string.IsNullOrWhiteSpace(info.UdpRuleName))
        {
            var udpRuleName = NormalizeRuleName(info.UdpRuleName, "McServerManager_UDP");
            RunNetsh(["advfirewall", "firewall", "delete", "rule", $"name={udpRuleName}"]);
        }
    }

    public void RecreateRules(int port, FirewallRuleInfo info)
    {
        DeleteRules(info);
        CreateRules(port, info);
    }

    private static void ValidatePort(int port)
    {
        if (port is < 1 or > 65535)
        {
            throw new InvalidOperationException($"不正なポート番号です: {port}");
        }
    }

    private static string NormalizeRuleName(string? value, string fallback)
    {
        var source = string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();
        var sanitized = UnsafeRuleNameChars.Replace(source, "_").Trim('_');
        if (string.IsNullOrWhiteSpace(sanitized))
        {
            sanitized = fallback;
        }

        return sanitized.Length > 120 ? sanitized[..120] : sanitized;
    }

    private void RunNetsh(IEnumerable<string> argumentList)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = "netsh",
            UseShellExecute = true,
            Verb = IsAdministrator() ? string.Empty : "runas",
            CreateNoWindow = true
        };
        foreach (var argument in argumentList)
        {
            startInfo.ArgumentList.Add(argument);
        }

        using var process = Process.Start(startInfo);
        if (process is null)
        {
            throw new InvalidOperationException("netsh の起動に失敗しました。");
        }

        process.WaitForExit();
        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException($"Firewall操作に失敗しました (ExitCode={process.ExitCode})。");
        }
    }
}
