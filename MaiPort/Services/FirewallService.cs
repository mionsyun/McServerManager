using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Security.Principal;
using System.Text;
using System.Text.RegularExpressions;
using MaiPort.Models;

namespace MaiPort.Services;

/// <summary>
/// netsh 経由で Windows ファイアウォールの受信規則を作成・削除する。
/// 非管理者で起動している場合は runas（UAC昇格）で netsh を実行する。
/// 1回の操作につき netsh スクリプトを1本だけ実行し、UACダイアログを1回に抑える。
/// </summary>
public sealed partial class FirewallService : IFirewallService
{
    private const string RuleNamePrefix = "MaiPort";
    private static readonly TimeSpan NetshTimeout = TimeSpan.FromSeconds(60);

    private enum RuleState
    {
        Present,
        Missing,
        Unknown
    }

    public bool IsAdministrator()
    {
        using var identity = WindowsIdentity.GetCurrent();
        var principal = new WindowsPrincipal(identity);
        return principal.IsInRole(WindowsBuiltInRole.Administrator);
    }

    public string BuildRuleName(int port, PortProtocol protocol)
    {
        ValidatePort(port);
        return $"{RuleNamePrefix}_{ToNetshProtocol(protocol)}_{port}";
    }

    public async Task<PortOperationResult> AllowAsync(int port, PortProtocol protocol, string description, CancellationToken ct = default)
    {
        ValidatePort(port);
        var label = NormalizeRuleToken(description, "Port");
        var commands = new List<string>();
        var ruleNames = new List<string>();

        foreach (var single in ExpandProtocols(protocol))
        {
            var ruleName = BuildRuleName(port, single);
            ruleNames.Add(ruleName);
            // 同名規則が残っていると重複登録になるため、作り直す。
            commands.Add($"advfirewall firewall delete rule name={ruleName}");
            commands.Add($"advfirewall firewall add rule name={ruleName} dir=in action=allow " +
                         $"protocol={ToNetshProtocol(single)} localport={port} profile=any description={RuleNamePrefix}_{label}");
        }

        var exitCode = await RunNetshScriptAsync(commands, ct).ConfigureAwait(false);
        if (exitCode is null)
        {
            return PortOperationResult.Failed("ファイアウォール設定がキャンセルされました（管理者権限が必要です）。");
        }

        var state = await GetRuleStateAsync(ruleNames, ct).ConfigureAwait(false);
        return state switch
        {
            RuleState.Present => PortOperationResult.Ok($"ファイアウォール: {DescribeProtocol(protocol)} {port} の受信を許可しました。"),
            RuleState.Missing => PortOperationResult.Failed(
                $"ファイアウォール受信規則を作成できませんでした (netsh ExitCode={exitCode})。管理者権限を許可したか確認してください。"),
            _ => PortOperationResult.Ok($"ファイアウォール: {DescribeProtocol(protocol)} {port} の受信許可を実行しました（規則の確認はできませんでした）。")
        };
    }

    public async Task<PortOperationResult> RemoveAsync(int port, PortProtocol protocol, CancellationToken ct = default)
    {
        ValidatePort(port);
        var commands = new List<string>();
        var ruleNames = new List<string>();

        foreach (var single in ExpandProtocols(protocol))
        {
            var ruleName = BuildRuleName(port, single);
            ruleNames.Add(ruleName);
            commands.Add($"advfirewall firewall delete rule name={ruleName}");
        }

        var exitCode = await RunNetshScriptAsync(commands, ct).ConfigureAwait(false);
        if (exitCode is null)
        {
            return PortOperationResult.Failed("ファイアウォール設定がキャンセルされました（管理者権限が必要です）。");
        }

        var state = await GetRuleStateAsync(ruleNames, ct).ConfigureAwait(false);
        return state == RuleState.Present
            ? PortOperationResult.Failed($"ファイアウォール受信規則を削除できませんでした (netsh ExitCode={exitCode})。")
            : PortOperationResult.Ok($"ファイアウォール: {DescribeProtocol(protocol)} {port} の受信規則を削除しました。");
    }

    /// <summary>
    /// netsh スクリプトを作成して実行し、終了コードを返す。UACがキャンセルされた場合は null。
    /// </summary>
    private async Task<int?> RunNetshScriptAsync(IReadOnlyList<string> commands, CancellationToken ct)
    {
        // netsh -f はスクリプトを ANSI として読むため、ASCII のみのコマンドを書き出す。
        var scriptPath = Path.Combine(Path.GetTempPath(), $"maiport_{Guid.NewGuid():N}.netsh");
        await File.WriteAllLinesAsync(scriptPath, commands, Encoding.ASCII, ct).ConfigureAwait(false);

        try
        {
            var elevated = IsAdministrator();
            var startInfo = new ProcessStartInfo
            {
                FileName = "netsh",
                UseShellExecute = !elevated,
                CreateNoWindow = elevated,
                WindowStyle = ProcessWindowStyle.Hidden
            };

            if (!elevated)
            {
                startInfo.Verb = "runas";
            }

            startInfo.ArgumentList.Add("-f");
            startInfo.ArgumentList.Add(scriptPath);

            using var process = Process.Start(startInfo);
            if (process is null)
            {
                throw new InvalidOperationException("netsh の起動に失敗しました。");
            }

            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(NetshTimeout);
            await process.WaitForExitAsync(timeout.Token).ConfigureAwait(false);
            return process.ExitCode;
        }
        catch (Win32Exception ex) when (ex.NativeErrorCode == 1223)
        {
            // ERROR_CANCELLED: ユーザーが UAC ダイアログをキャンセルした。
            return null;
        }
        finally
        {
            TryDeleteFile(scriptPath);
        }
    }

    /// <summary>
    /// 指定した規則がすべて存在するかを確認する。確認自体ができない場合は Unknown。
    /// </summary>
    private static async Task<RuleState> GetRuleStateAsync(IReadOnlyList<string> ruleNames, CancellationToken ct)
    {
        var anyMissing = false;
        foreach (var ruleName in ruleNames)
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = "netsh",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };
            foreach (var argument in new[] { "advfirewall", "firewall", "show", "rule", $"name={ruleName}" })
            {
                startInfo.ArgumentList.Add(argument);
            }

            try
            {
                using var process = Process.Start(startInfo);
                if (process is null)
                {
                    return RuleState.Unknown;
                }

                var output = await process.StandardOutput.ReadToEndAsync(ct).ConfigureAwait(false);
                await process.WaitForExitAsync(ct).ConfigureAwait(false);

                if (output.Contains(ruleName, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                // 規則が無いときの netsh 出力（"No rules match the specified criteria."）を判定する。
                // 権限不足などで確認できない場合は Missing と断定せず Unknown を返す。
                if (NoRuleMatchPattern().IsMatch(output))
                {
                    anyMissing = true;
                    continue;
                }

                return RuleState.Unknown;
            }
            catch (Exception ex) when (ex is Win32Exception or InvalidOperationException)
            {
                return RuleState.Unknown;
            }
        }

        return anyMissing ? RuleState.Missing : RuleState.Present;
    }

    private static void TryDeleteFile(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (IOException)
        {
            // 一時ファイルの削除失敗は無視する。
        }
    }

    private static IEnumerable<PortProtocol> ExpandProtocols(PortProtocol protocol)
    {
        return protocol switch
        {
            PortProtocol.Tcp => new[] { PortProtocol.Tcp },
            PortProtocol.Udp => new[] { PortProtocol.Udp },
            _ => new[] { PortProtocol.Tcp, PortProtocol.Udp }
        };
    }

    private static string ToNetshProtocol(PortProtocol protocol)
    {
        return protocol switch
        {
            PortProtocol.Tcp => "TCP",
            PortProtocol.Udp => "UDP",
            _ => "ANY"
        };
    }

    private static string DescribeProtocol(PortProtocol protocol)
    {
        return protocol == PortProtocol.Both ? "TCP/UDP" : ToNetshProtocol(protocol);
    }

    private static void ValidatePort(int port)
    {
        if (port is < 1 or > 65535)
        {
            throw new ArgumentOutOfRangeException(nameof(port), port, "ポート番号は 1〜65535 で指定してください。");
        }
    }

    /// <summary>
    /// 規則名・説明に使えない文字を除去する（netsh へのオプション注入を防ぐ）。
    /// </summary>
    internal static string NormalizeRuleToken(string? value, string fallback)
    {
        var source = string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();
        var sanitized = UnsafeRuleNameChars().Replace(source, "_").Trim('_');
        if (string.IsNullOrWhiteSpace(sanitized))
        {
            sanitized = fallback;
        }

        return sanitized.Length > 60 ? sanitized[..60] : sanitized;
    }

    [GeneratedRegex(@"[^A-Za-z0-9_\-\.]")]
    private static partial Regex UnsafeRuleNameChars();

    [GeneratedRegex(@"No rules match|一致する規則はありません", RegexOptions.IgnoreCase)]
    private static partial Regex NoRuleMatchPattern();
}
