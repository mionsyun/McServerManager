using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Net.Http;
using System.Diagnostics;
using McServerManager.Models;

namespace McServerManager.Services;

public sealed class NetworkService : INetworkService
{
    public IReadOnlyList<string> GetLanIpAddresses()
    {
        var list = new List<string>();
        foreach (var networkInterface in NetworkInterface.GetAllNetworkInterfaces())
        {
            if (networkInterface.OperationalStatus != OperationalStatus.Up)
            {
                continue;
            }

            var ipProps = networkInterface.GetIPProperties();
            foreach (var address in ipProps.UnicastAddresses)
            {
                if (address.Address.AddressFamily == AddressFamily.InterNetwork &&
                    !IPAddress.IsLoopback(address.Address))
                {
                    list.Add(address.Address.ToString());
                }
            }
        }

        return list;
    }

    public IReadOnlyList<string> GetExternalChecklist(NetworkProtocol protocol = NetworkProtocol.Tcp)
    {
        var protocolLabel = protocol == NetworkProtocol.Udp ? "UDP" : "TCP";
        return new[]
        {
            $"ルーターで {protocolLabel} ポートの開放（転送）を設定する。",
            "グローバル IP とポート番号をフレンドに共有する。",
            "サーバーが起動中であることを確認する。",
            "Windows Firewall の受信許可を確認する。"
        };
    }

    public async Task<string?> GetPublicIpAsync()
    {
        try
        {
            using var client = new HttpClient
            {
                Timeout = TimeSpan.FromSeconds(4)
            };
            var ip = await client.GetStringAsync(ExternalApiUrls.PublicIpApi).ConfigureAwait(false);
            return string.IsNullOrWhiteSpace(ip) ? null : ip.Trim();
        }
        catch
        {
            return null;
        }
    }

    public IReadOnlyList<Process> GetProcessesUsingPort(int port, NetworkProtocol protocol = NetworkProtocol.Tcp)
    {
        var pids = GetProcessIdsUsingPort(port, protocol);
        var list = new List<Process>();
        foreach (var pid in pids)
        {
            try
            {
                list.Add(Process.GetProcessById(pid));
            }
            catch
            {
                // Process might have exited.
            }
        }

        return list;
    }

    public bool TryKillProcessesUsingPort(int port, out string? error, NetworkProtocol protocol = NetworkProtocol.Tcp)
    {
        error = null;
        var processes = GetProcessesUsingPort(port, protocol);
        if (processes.Count == 0)
        {
            return true;
        }

        foreach (var process in processes)
        {
            try
            {
                process.Kill(true);
            }
            catch (Exception ex)
            {
                error = ex.Message;
                return false;
            }
        }

        return true;
    }

    private static IReadOnlyList<int> GetProcessIdsUsingPort(int port, NetworkProtocol protocol)
    {
        var protocolName = protocol == NetworkProtocol.Udp ? "UDP" : "TCP";
        var list = new List<int>();
        try
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = "netstat",
                Arguments = $"-ano -p {protocolName.ToLowerInvariant()}",
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var process = Process.Start(startInfo);
            if (process is null)
            {
                return list;
            }

            var output = process.StandardOutput.ReadToEnd();
            process.WaitForExit(2000);
            var portToken = ":" + port;
            foreach (var line in output.Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries))
            {
                var trimmed = line.Trim();
                if (!trimmed.StartsWith(protocolName, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                // TCP: "TCP  0.0.0.0:25565  0.0.0.0:0  LISTENING  1234"
                // UDP: "UDP  0.0.0.0:19132  *:*  1234"（状態列なし。バインドしていれば使用中）
                var parts = trimmed.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length < (protocol == NetworkProtocol.Udp ? 4 : 5))
                {
                    continue;
                }

                var local = parts[1];
                var pidText = parts[^1];
                if (protocol == NetworkProtocol.Tcp
                    && !parts[3].Equals("LISTENING", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (!local.EndsWith(portToken, StringComparison.Ordinal))
                {
                    continue;
                }

                if (int.TryParse(pidText, out var pid))
                {
                    list.Add(pid);
                }
            }
        }
        catch
        {
            // Ignore failures and report no listeners.
        }

        return list.Distinct().ToList();
    }
}

