using McServerManager.Models;
namespace McServerManager.Services;
public interface IBackupScheduleRestorer
{
    void Restore(AppSettings settings);
}
