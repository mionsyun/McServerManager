using McServerManager.Models;

namespace McServerManager.Services;

public interface IAppUpdateService
{
    Task<AppUpdateCheckResult> CheckForUpdatesAsync(CancellationToken cancellationToken = default);
    Task<AppUpdateDownloadResult> DownloadAndLaunchInstallerAsync(AppUpdateManifest manifest, CancellationToken cancellationToken = default);
}
