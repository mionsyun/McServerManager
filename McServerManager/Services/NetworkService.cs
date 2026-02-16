using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Net.Http;
using System.Diagnostics;

namespace McServerManager.Services;

public sealed class NetworkService
{
    public async Task<bool> IsLocalPortOpenAsync(int port)
    {
        try
        {
            using var client = new TcpClient();
            var connectTask = client.ConnectAsync(IPAddress.Loopback, port);
            var completed = await Task.WhenAny(connectTask, Task.Delay(1000)).ConfigureAwait(false);
            return completed == connectTask && client.Connected;
        }
        catch
        {
            return false;
        }
    }

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

    public IReadOnlyList<string> GetExternalChecklist()
    {
        return new[]
        {
            "ルーターでポート開放（転送）を設定する。",
            "グローバル IP とポート番号をフレンドに共有する。",
            "サーバーが起動中で待受中か確認する。",
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
            var ip = await client.GetStringAsync("https://api.ipify.org").ConfigureAwait(false);
            return string.IsNullOrWhiteSpace(ip) ? null : ip.Trim();
        }
        catch
        {
            return null;
        }
    }

    public IReadOnlyList<Process> GetProcessesUsingPort(int port)
    {
        var pids = GetProcessIdsUsingPort(port);
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

    public bool TryKillProcessesUsingPort(int port, out string? error)
    {
        error = null;
        var processes = GetProcessesUsingPort(port);
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

    private static IReadOnlyList<int> GetProcessIdsUsingPort(int port)
    {
        var list = new List<int>();
        try
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = "netstat",
                Arguments = "-ano -p tcp",
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
                if (!trimmed.StartsWith("TCP", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var parts = trimmed.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length < 5)
                {
                    continue;
                }

                var local = parts[1];
                var state = parts[3];
                var pidText = parts[^1];
                if (!state.Equals("LISTENING", StringComparison.OrdinalIgnoreCase))
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

