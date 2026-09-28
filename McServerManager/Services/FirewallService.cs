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

    public void CreateProgramRules(string programPath, FirewallRuleInfo info)
    {
        var program = ValidateProgramPath(programPath);
        var tcpRuleName = NormalizeRuleName(info.TcpRuleName, "McServerManager_TCP");
        var udpRuleName = NormalizeRuleName(info.UdpRuleName, "McServerManager_UDP");

        RunNetsh(["advfirewall", "firewall", "add", "rule", $"name={tcpRuleName}", "dir=in", "action=allow", "protocol=TCP", $"program={program}", "profile=any", "enable=yes"]);
        RunNetsh(["advfirewall", "firewall", "add", "rule", $"name={udpRuleName}", "dir=in", "action=allow", "protocol=UDP", $"program={program}", "profile=any", "enable=yes"]);
    }

    public void RecreateProgramRules(string programPath, FirewallRuleInfo info)
    {
        DeleteRules(info);
        CreateProgramRules(programPath, info);
    }

    public void ApplyServerRules(ServerConfig config, bool recreate)
    {
        if (string.IsNullOrWhiteSpace(config.Firewall.TcpRuleName))
        {
            config.Firewall = BuildRuleInfo(config.ServerId);
        }

        if (ServerEditions.IsBedrock(config))
        {
            // NetherNet のゲーム通信 UDP は接続ごとに割り当てられ、LAN 検出に UDP 7551 も使うため、
            // ポート指定ではなく実行ファイル単位で許可する
            var program = Path.Combine(config.DirectoryPath, ServerEditions.BedrockExecutableName);
            if (recreate)
                RecreateProgramRules(program, config.Firewall);
            else
                CreateProgramRules(program, config.Firewall);
            return;
        }

        if (recreate)
            RecreateRules(config.Port, config.Firewall);
        else
            CreateRules(config.Port, config.Firewall);
    }

    /// <summary>許可対象は統合版サーバーの実行ファイルに限定する（任意プログラムの許可を防ぐ）。</summary>
    private static string ValidateProgramPath(string programPath)
    {
        var fullPath = Path.GetFullPath(programPath);
        if (!string.Equals(Path.GetFileName(fullPath), ServerEditions.BedrockExecutableName, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException($"許可できない実行ファイルです: {fullPath}");
        }

        if (!File.Exists(fullPath))
        {
            throw new FileNotFoundException("許可する実行ファイルが見つかりません。", fullPath);
        }

        return fullPath;
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
