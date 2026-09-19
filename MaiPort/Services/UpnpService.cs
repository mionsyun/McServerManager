using MaiPort.Models;
using Open.Nat;

namespace MaiPort.Services;

/// <summary>
/// Open.NAT を用いた UPnP ポートマッピング。TCP / UDP の双方に対応する。
/// </summary>
public sealed class UpnpService : IUpnpService
{
    private static readonly TimeSpan DiscoveryTimeout = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan CacheDuration = TimeSpan.FromMinutes(2);

    private readonly NatDiscoverer _discoverer = new();
    private NatDevice? _device;
    private DateTime _lastDiscoveryUtc = DateTime.MinValue;

    public async Task<PortOperationResult> OpenAsync(int port, PortProtocol protocol, string description, CancellationToken ct = default)
    {
        var device = await GetDeviceAsync(ct).ConfigureAwait(false);
        if (device is null)
        {
            return PortOperationResult.Failed(BuildDeviceNotFoundMessage(port, protocol));
        }

        var label = string.IsNullOrWhiteSpace(description) ? "MaiPort" : description.Trim();
        foreach (var natProtocol in ResolveProtocols(protocol))
        {
            try
            {
                var mapping = new Mapping(natProtocol, port, port, $"MaiPort - {label}");
                await device.CreatePortMapAsync(mapping).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                return PortOperationResult.Failed(BuildOpenFailureMessage(port, natProtocol, ex));
            }
        }

        return PortOperationResult.Ok($"UPnP: {DescribeProtocol(protocol)} {port} を開放しました。");
    }

    public async Task<PortOperationResult> CloseAsync(int port, PortProtocol protocol, CancellationToken ct = default)
    {
        var device = await GetDeviceAsync(ct).ConfigureAwait(false);
        if (device is null)
        {
            return PortOperationResult.Failed("UPnP対応ルーターが見つからないため、マッピングを削除できませんでした。");
        }

        var failures = new List<string>();
        foreach (var natProtocol in ResolveProtocols(protocol))
        {
            try
            {
                var mapping = new Mapping(natProtocol, port, port);
                await device.DeletePortMapAsync(mapping).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                // 既に存在しないマッピングの削除は失敗しうるため、まとめて報告する。
                failures.Add($"{natProtocol.ToString().ToUpperInvariant()}: {ex.Message}");
            }
        }

        return failures.Count == 0
            ? PortOperationResult.Ok($"UPnP: {DescribeProtocol(protocol)} {port} のマッピングを削除しました。")
            : PortOperationResult.Failed($"UPnPマッピングの削除に失敗しました。{Environment.NewLine}{string.Join(Environment.NewLine, failures)}");
    }

    public async Task<bool> IsDeviceAvailableAsync(CancellationToken ct = default)
    {
        return await GetDeviceAsync(ct).ConfigureAwait(false) is not null;
    }

    public async Task<string?> GetExternalIpAsync(CancellationToken ct = default)
    {
        var device = await GetDeviceAsync(ct).ConfigureAwait(false);
        if (device is null)
        {
            return null;
        }

        try
        {
            var address = await device.GetExternalIPAsync().ConfigureAwait(false);
            return address?.ToString();
        }
        catch
        {
            return null;
        }
    }

    private async Task<NatDevice?> GetDeviceAsync(CancellationToken ct)
    {
        if (_device is not null && DateTime.UtcNow - _lastDiscoveryUtc < CacheDuration)
        {
            return _device;
        }

        try
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(DiscoveryTimeout);
            _device = await _discoverer.DiscoverDeviceAsync(PortMapper.Upnp, cts).ConfigureAwait(false);
            _lastDiscoveryUtc = DateTime.UtcNow;
            return _device;
        }
        catch
        {
            // ルーターが UPnP 非対応 / 無効の場合は例外になる。呼び出し側でメッセージ化する。
            _device = null;
            return null;
        }
    }

    private static IEnumerable<Protocol> ResolveProtocols(PortProtocol protocol)
    {
        return protocol switch
        {
            PortProtocol.Tcp => new[] { Protocol.Tcp },
            PortProtocol.Udp => new[] { Protocol.Udp },
            _ => new[] { Protocol.Tcp, Protocol.Udp }
        };
    }

    private static string DescribeProtocol(PortProtocol protocol)
    {
        return protocol switch
        {
            PortProtocol.Tcp => "TCP",
            PortProtocol.Udp => "UDP",
            _ => "TCP/UDP"
        };
    }

    private static string BuildDeviceNotFoundMessage(int port, PortProtocol protocol)
    {
        var lines = new List<string>
        {
            "UPnP対応ルーターが見つかりませんでした。",
            "共有回線（J:COMなど）やCGNAT環境では、ポート開放そのものができない場合があります。",
            $"ルーターの設定画面で {DescribeProtocol(protocol)} {port} のポート転送を手動設定するか、",
            "固定IPオプション / VPN中継（Tailscale, playit.gg など）を検討してください。"
        };

        return string.Join(Environment.NewLine, lines);
    }

    private static string BuildOpenFailureMessage(int port, Protocol natProtocol, Exception ex)
    {
        var detail = string.IsNullOrWhiteSpace(ex.Message) ? ex.GetType().Name : ex.Message;
        var protocolName = natProtocol.ToString().ToUpperInvariant();

        var lines = new List<string>
        {
            $"UPnPで {protocolName} {port} の開放に失敗しました。",
            $"詳細: {detail}"
        };

        if (IsLikelySharedLineFailure(detail))
        {
            lines.Add("回線事業者側で外部公開が制限されている可能性があります（共有回線/CGNAT）。");
        }

        lines.Add("ルーターのUPnP有効化・二重ルーター構成（ONU+Wi-Fiルーター）の確認を行ってください。");

        return string.Join(Environment.NewLine, lines);
    }

    private static bool IsLikelySharedLineFailure(string detail)
    {
        if (string.IsNullOrWhiteSpace(detail))
        {
            return false;
        }

        return detail.Contains("718", StringComparison.OrdinalIgnoreCase)
               || detail.Contains("NotAuthorized", StringComparison.OrdinalIgnoreCase)
               || detail.Contains("NoSuchEntryInArray", StringComparison.OrdinalIgnoreCase)
               || detail.Contains("ActionFailed", StringComparison.OrdinalIgnoreCase)
               || detail.Contains("アクセス", StringComparison.OrdinalIgnoreCase)
               || detail.Contains("拒否", StringComparison.OrdinalIgnoreCase);
    }
}
