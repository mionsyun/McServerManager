using System.Text.Json;
using McServerManager.Models;

namespace McServerManager.Services;

public sealed class AppSettingsService : IAppSettingsService
{
    private readonly AppPathsService _pathsService;
    private readonly JsonSerializerOptions _options = new() { WriteIndented = true };

    public AppSettingsService(AppPathsService pathsService)
    {
        _pathsService = pathsService;
    }

    public AppSettings Load()
    {
        try
        {
            if (!File.Exists(_pathsService.AppSettingsPath))
            {
                return new AppSettings();
            }

            var json = File.ReadAllText(_pathsService.AppSettingsPath);
            return JsonSerializer.Deserialize<AppSettings>(json) ?? new AppSettings();
        }
        catch
        {
            return new AppSettings();
        }
    }

    public void Save(AppSettings settings)
    {
        var json = JsonSerializer.Serialize(settings, _options);
        File.WriteAllText(_pathsService.AppSettingsPath, json);
    }
}
