using McServerManager.Models;

namespace McServerManager.Services;

public interface IAppSettingsService
{
    AppSettings Load();
    void Save(AppSettings settings);
}
