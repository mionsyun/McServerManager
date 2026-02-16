using Open.Nat;

namespace McServerManager.Services;

public sealed class UpnpService
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
            return (false, "自動ポート開放に対応するルーターが見つかりません。");
        }

        try
        {
            var mapping = new Mapping(Protocol.Tcp, port, port, description);
            await device.CreatePortMapAsync(mapping).ConfigureAwait(false);
            return (true, null);
        }
        catch (Exception ex)
        {
            return (false, ex.Message);
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
}
