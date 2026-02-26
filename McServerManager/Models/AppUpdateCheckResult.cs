namespace McServerManager.Models;

public enum AppUpdateCheckStatus
{
    UpToDate,
    UpdateAvailable,
    Failed
}

public sealed class AppUpdateCheckResult
{
    public AppUpdateCheckStatus Status { get; private init; }
    public AppUpdateManifest? Manifest { get; private init; }
    public string? ErrorMessage { get; private init; }

    public static AppUpdateCheckResult UpToDate()
    {
        return new AppUpdateCheckResult
        {
            Status = AppUpdateCheckStatus.UpToDate
        };
    }

    public static AppUpdateCheckResult UpdateAvailable(AppUpdateManifest manifest)
    {
        return new AppUpdateCheckResult
        {
            Status = AppUpdateCheckStatus.UpdateAvailable,
            Manifest = manifest
        };
    }

    public static AppUpdateCheckResult Failed(string message)
    {
        return new AppUpdateCheckResult
        {
            Status = AppUpdateCheckStatus.Failed,
            ErrorMessage = message
        };
    }
}
