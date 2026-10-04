using System.IO;

namespace McServerManager.Services;

public enum AppSettingsStorageError
{
    InvalidData,
    UnsupportedVersion,
    TooLarge,
    Busy,
    Conflict,
    ReadFailed,
    WriteFailed
}

/// <summary>A settings failure that must be shown to the user, never replaced by writable defaults.</summary>
public sealed class AppSettingsStorageException : IOException
{
    public AppSettingsStorageException(AppSettingsStorageError reason, Exception? innerException = null)
        : base(GetMessage(reason), innerException)
    {
        Reason = reason;
    }

    public AppSettingsStorageError Reason { get; }

    private static string GetMessage(AppSettingsStorageError reason) => reason switch
    {
        AppSettingsStorageError.InvalidData => "The settings file is invalid. It has not been reset. Check the settings file and its backup before continuing.",
        AppSettingsStorageError.UnsupportedVersion => "The settings file uses an unsupported format. Open it with a compatible version of MaiPilot.",
        AppSettingsStorageError.TooLarge => "The settings file exceeds the supported size. It has not been reset.",
        AppSettingsStorageError.Busy => "Another MaiPilot instance is using the settings file. Close it or retry shortly.",
        AppSettingsStorageError.Conflict => "The settings have changed since they were loaded. Reload them before saving again.",
        AppSettingsStorageError.ReadFailed => "The settings file could not be read. It has not been reset.",
        _ => "The settings could not be saved. Check the settings file and its backup before retrying."
    };
}
