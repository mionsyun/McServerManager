using McServerManager.Models;

namespace McServerManager.Services;

public interface IBackupSchedulerService
{
    void ApplyConfiguration(ServerConfig config);
    void Unschedule(string serverId);
    DateTime? GetNextRunAt(string serverId);
    void UnscheduleAll();
}
