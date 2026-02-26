namespace McServerManager.Models;

public enum AppUpdateDownloadStatus
{
    Success,
    HashMismatch,
    SignatureInvalid,
    DownloadFailed
}

public sealed class AppUpdateDownloadResult
{
    public AppUpdateDownloadStatus Status { get; private init; }
    public string? InstallerPath { get; private init; }
    public string? ErrorMessage { get; private init; }

    public static AppUpdateDownloadResult Success(string installerPath)
    {
        return new AppUpdateDownloadResult
        {
            Status = AppUpdateDownloadStatus.Success,
            InstallerPath = installerPath
        };
    }

    public static AppUpdateDownloadResult HashMismatch(string message)
    {
        return new AppUpdateDownloadResult
        {
            Status = AppUpdateDownloadStatus.HashMismatch,
            ErrorMessage = message
        };
    }

    public static AppUpdateDownloadResult SignatureInvalid(string message)
    {
        return new AppUpdateDownloadResult
        {
            Status = AppUpdateDownloadStatus.SignatureInvalid,
            ErrorMessage = message
        };
    }

    public static AppUpdateDownloadResult DownloadFailed(string message)
    {
        return new AppUpdateDownloadResult
        {
            Status = AppUpdateDownloadStatus.DownloadFailed,
            ErrorMessage = message
        };
    }
}
