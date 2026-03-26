using System.Diagnostics;
using System.Security.Principal;
using McServerManager.Models;

namespace McServerManager.Services;

public sealed class FirewallService : IFirewallService
{
    public bool IsAdministrator()
    {
        using var identity = WindowsIdentity.GetCurrent();
        var principal = new WindowsPrincipal(identity);
        return principal.IsInRole(WindowsBuiltInRole.Administrator);
    }

    public FirewallRuleInfo BuildRuleInfo(string serverId)
    {
        return new FirewallRuleInfo
        {
            TcpRuleName = $"McServerManager_{serverId}_TCP",
            UdpRuleName = $"McServerManager_{serverId}_UDP"
        };
    }

    public void CreateRules(int port, FirewallRuleInfo info)
    {
        RunNetsh($"advfirewall firewall add rule name=\"{info.TcpRuleName}\" dir=in action=allow protocol=TCP localport={port}");
        RunNetsh($"advfirewall firewall add rule name=\"{info.UdpRuleName}\" dir=in action=allow protocol=UDP localport={port}");
    }

    public void DeleteRules(FirewallRuleInfo info)
    {
        if (!string.IsNullOrWhiteSpace(info.TcpRuleName))
        {
            RunNetsh($"advfirewall firewall delete rule name=\"{info.TcpRuleName}\"");
        }

        if (!string.IsNullOrWhiteSpace(info.UdpRuleName))
        {
            RunNetsh($"advfirewall firewall delete rule name=\"{info.UdpRuleName}\"");
        }
    }

    public void RecreateRules(int port, FirewallRuleInfo info)
    {
        DeleteRules(info);
        CreateRules(port, info);
    }

    private void RunNetsh(string arguments)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = "netsh",
            Arguments = arguments,
            UseShellExecute = true,
            Verb = IsAdministrator() ? string.Empty : "runas",
            CreateNoWindow = true
        };

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
