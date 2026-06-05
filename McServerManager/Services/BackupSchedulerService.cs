using System.Collections.Concurrent;
using McServerManager.Models;

namespace McServerManager.Services;

public sealed class BackupSchedulerService : IBackupSchedulerService, IDisposable
{
    private readonly IWorldService _worldService;
    private readonly IServerConfigService _serverConfigService;
    private readonly ConcurrentDictionary<string, ScheduledEntry> _entries = new(StringComparer.OrdinalIgnoreCase);
    private bool _disposed;

    public BackupSchedulerService(IWorldService worldService, IServerConfigService serverConfigService)
    {
        _worldService = worldService;
        _serverConfigService = serverConfigService;
    }

    public void ApplyConfiguration(ServerConfig config)
    {
        if (string.IsNullOrWhiteSpace(config.ServerId))
        {
            return;
        }

        if (!config.ScheduledBackupEnabled || config.ScheduledBackupIntervalMinutes <= 0)
        {
            Unschedule(config.ServerId);
            return;
        }

        var intervalMs = (long)config.ScheduledBackupIntervalMinutes * 60_000L;
        var nextRunAt = DateTime.UtcNow.AddMilliseconds(intervalMs);
        var entry = new ScheduledEntry(config.ServerId, nextRunAt);
        entry.Timer = new System.Threading.Timer(_ => OnTick(entry), null, intervalMs, intervalMs);

        if (_entries.TryRemove(config.ServerId, out var previous))
        {
            previous.Timer?.Dispose();
        }
        _entries[config.ServerId] = entry;
    }

    public void Unschedule(string serverId)
    {
        if (_entries.TryRemove(serverId, out var entry))
        {
            entry.Timer?.Dispose();
        }
    }

    public void UnscheduleAll()
    {
        foreach (var key in _entries.Keys.ToList())
        {
            Unschedule(key);
        }
    }

    public DateTime? GetNextRunAt(string serverId)
    {
        return _entries.TryGetValue(serverId, out var entry) ? entry.NextRunAtUtc : null;
    }

    private void OnTick(ScheduledEntry entry)
    {
        if (_disposed)
        {
            return;
        }

        var config = _serverConfigService.Load(entry.ServerId);
        if (config is null || !config.ScheduledBackupEnabled)
        {
            Unschedule(entry.ServerId);
            return;
        }

        try
        {
            var serverDirectory = config.DirectoryPath;
            if (string.IsNullOrWhiteSpace(serverDirectory) || !Directory.Exists(serverDirectory))
            {
                return;
            }

            var worldName = string.IsNullOrWhiteSpace(config.WorldName) ? "world" : config.WorldName;
            var worldPath = Path.Combine(serverDirectory, worldName);
            if (!Directory.Exists(worldPath))
            {
                return;
            }

            _worldService.CreateAutomaticWorldBackup(serverDirectory, worldName, config.ScheduledBackupMaxRetention);

            config.LastScheduledBackupAt = DateTime.UtcNow;
            _serverConfigService.Save(config);
        }
        catch
        {
            // 定期実行の失敗はログのみで握りつぶす（次回リトライ）。
        }
        finally
        {
            entry.NextRunAtUtc = DateTime.UtcNow.AddMinutes(Math.Max(1, config?.ScheduledBackupIntervalMinutes ?? 60));
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }
        _disposed = true;
        UnscheduleAll();
    }

    private sealed class ScheduledEntry
    {
        public ScheduledEntry(string serverId, DateTime nextRunAtUtc)
        {
            ServerId = serverId;
            NextRunAtUtc = nextRunAtUtc;
        }

        public string ServerId { get; }
        public DateTime NextRunAtUtc { get; set; }
        public System.Threading.Timer? Timer { get; set; }
    }
}
