using Open.Nat;

namespace McServerManager.Services;

public sealed class UpnpService : IUpnpService
{
    private readonly NatDiscoverer _discoverer = new();
    private NatDevice? _device;
    private DateTime _lastDiscovery = DateTime.MinValue;
    private readonly TimeSpan _cacheDuration = TimeSpan.FromMinutes(2);

    public async Task<(bool ok, string? error)> TryOpenPortAsync(int port, string description)
    {
        var device = await GetDeviceAsync().ConfigureAwait(false);
        if (device is null)
        {
            return (false, BuildDeviceNotFoundMessage(port));
        }

        try
        {
            var mapping = new Mapping(Protocol.Tcp, port, port, description);
            await device.CreatePortMapAsync(mapping).ConfigureAwait(false);
            return (true, null);
        }
        catch (Exception ex)
        {
            return (false, BuildOpenPortFailureMessage(port, ex));
        }
    }

    public async Task TryClosePortAsync(int port)
    {
        var device = await GetDeviceAsync().ConfigureAwait(false);
        if (device is null)
        {
            return;
        }

        try
        {
            var mapping = new Mapping(Protocol.Tcp, port, port);
            await device.DeletePortMapAsync(mapping).ConfigureAwait(false);
        }
        catch
        {
            // Ignore failures on cleanup.
        }
    }

    private async Task<NatDevice?> GetDeviceAsync()
    {
        if (_device is not null && DateTime.UtcNow - _lastDiscovery < _cacheDuration)
        {
            return _device;
        }

        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(3));
            _device = await _discoverer.DiscoverDeviceAsync(PortMapper.Upnp, cts).ConfigureAwait(false);
            _lastDiscovery = DateTime.UtcNow;
            return _device;
        }
        catch
        {
            return null;
        }
    }

    private static string BuildDeviceNotFoundMessage(int port)
    {
        var lines = new List<string>
        {
            "UPnP対応ルーターが見つかりませんでした。",
            "共有回線（J:COMなど）やCGNAT環境では、ポート開放そのものができない場合があります。",
            $"手動でルーターの TCP {port} を開放するか、固定IPオプション / VPN中継（Tailscale, playit.gg など）を検討してください。"
        };

        return string.Join(Environment.NewLine, lines);
    }

    private static string BuildOpenPortFailureMessage(int port, Exception ex)
    {
        var detail = string.IsNullOrWhiteSpace(ex.Message)
            ? ex.GetType().Name
            : ex.Message;

        var lines = new List<string>
        {
            $"UPnPで TCP {port} の開放に失敗しました。",
            $"詳細: {detail}"
        };

        if (IsLikelySharedLineFailure(detail))
        {
            lines.Add("回線事業者側で外部公開が制限されている可能性があります（共有回線/CGNAT）。");
        }

        lines.Add("ルーターのUPnP有効化・二重ルーター構成（ONU+Wi-Fiルーター）の確認を行ってください。");
        lines.Add("改善しない場合は、固定IPオプションまたはVPN中継（Tailscale, playit.gg など）の利用を推奨します。");

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
