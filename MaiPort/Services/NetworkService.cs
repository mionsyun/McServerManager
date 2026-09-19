using System.ComponentModel;
using System.Diagnostics;
using System.Net;
using System.Net.Http;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using MaiPort.Models;

namespace MaiPort.Services;

/// <summary>
/// ローカル IP / グローバル IP / ポート使用状況を取得する。
/// </summary>
public sealed class NetworkService : INetworkService
{
    private const string PublicIpApi = "https://api.ipify.org";
    private static readonly TimeSpan HttpTimeout = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan NetstatTimeout = TimeSpan.FromSeconds(5);

    public IReadOnlyList<string> GetLanIpAddresses()
    {
        var list = new List<string>();
        foreach (var networkInterface in NetworkInterface.GetAllNetworkInterfaces())
        {
            if (networkInterface.OperationalStatus != OperationalStatus.Up ||
                networkInterface.NetworkInterfaceType == NetworkInterfaceType.Loopback)
            {
                continue;
            }

            foreach (var address in networkInterface.GetIPProperties().UnicastAddresses)
            {
                if (address.Address.AddressFamily == AddressFamily.InterNetwork &&
                    !IPAddress.IsLoopback(address.Address))
                {
                    list.Add(address.Address.ToString());
                }
            }
        }

        return list.Distinct().ToList();
    }

    public async Task<string?> GetPublicIpAsync(CancellationToken ct = default)
    {
        try
        {
            using var client = new HttpClient { Timeout = HttpTimeout };
            var ip = await client.GetStringAsync(PublicIpApi, ct).ConfigureAwait(false);
            return string.IsNullOrWhiteSpace(ip) ? null : ip.Trim();
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            return null;
        }
    }

    public IReadOnlyList<PortListener> GetListeners(int port, PortProtocol protocol)
    {
        var listeners = new List<PortListener>();
        foreach (var single in ExpandProtocols(protocol))
        {
            listeners.AddRange(GetListenersCore(port, single));
        }

        return listeners;
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

    private static IReadOnlyList<PortListener> GetListenersCore(int port, PortProtocol protocol)
    {
        var results = new List<PortListener>();
        var protocolArgument = protocol == PortProtocol.Tcp ? "tcp" : "udp";

        try
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = "netstat",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                CreateNoWindow = true
            };
            foreach (var argument in new[] { "-ano", "-p", protocolArgument })
            {
                startInfo.ArgumentList.Add(argument);
            }

            using var process = Process.Start(startInfo);
            if (process is null)
            {
                return results;
            }

            var output = process.StandardOutput.ReadToEnd();
            process.WaitForExit((int)NetstatTimeout.TotalMilliseconds);

            foreach (var line in output.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
            {
                var parts = line.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length < 4 || !parts[0].Equals(protocolArgument, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var localEndPoint = parts[1];
                if (GetPortFromEndPoint(localEndPoint) != port)
                {
                    continue;
                }

                // TCP は LISTENING 行のみを対象にする（UDP には状態列が無い）。
                if (protocol == PortProtocol.Tcp &&
                    !parts[3].Equals("LISTENING", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (!int.TryParse(parts[^1], out var pid))
                {
                    continue;
                }

                results.Add(new PortListener(protocol, localEndPoint, pid, GetProcessName(pid)));
            }
        }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException)
        {
            // netstat を実行できない環境では使用状況を表示しない。
        }

        return results;
    }

    internal static int GetPortFromEndPoint(string endPoint)
    {
        var separatorIndex = endPoint.LastIndexOf(':');
        if (separatorIndex < 0 || separatorIndex == endPoint.Length - 1)
        {
            return -1;
        }

        return int.TryParse(endPoint[(separatorIndex + 1)..], out var port) ? port : -1;
    }

    private static string GetProcessName(int pid)
    {
        try
        {
            using var process = Process.GetProcessById(pid);
            return process.ProcessName;
        }
        catch (ArgumentException)
        {
            return "(終了済み)";
        }
        catch (InvalidOperationException)
        {
            return "(不明)";
        }
    }
}
