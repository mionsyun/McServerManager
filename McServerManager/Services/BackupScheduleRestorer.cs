using McServerManager.Models;
namespace McServerManager.Services;

/// <summary>Retains scheduled backups for explicitly registered external server roots across editions.</summary>
public sealed class BackupScheduleRestorer(IServerConfigService configurations, IBackupSchedulerService scheduler) : IBackupScheduleRestorer
{
    public void Restore(AppSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        foreach (var config in configurations.LoadAll(settings.ServerDirectories))
            if (config.ScheduledBackupEnabled) scheduler.ApplyConfiguration(config);
    }
}
