using System.IO;

namespace McServerManager.Services;

public sealed class AppPathsService
{
    public AppPathsService()
    {
        var appDataPath = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        var legacyRootPath = Path.Combine(appDataPath, "McServerManager");
        RootPath = Path.Combine(appDataPath, "BlockPilot");
        MigrateLegacyData(legacyRootPath, RootPath);
        ServersPath = Path.Combine(RootPath, "servers");
        CachePath = Path.Combine(RootPath, "cache");
        EnsureDirectories();
    }

    public string RootPath { get; }
    public string ServersPath { get; }
    public string CachePath { get; }

    public string AppSettingsPath => Path.Combine(RootPath, "appsettings.json");

    public string GetServerPath(string serverId)
    {
        return Path.Combine(ServersPath, serverId);
    }

    public string GetServerConfigPath(string serverId)
    {
        return Path.Combine(GetServerPath(serverId), "config.json");
    }

    public void EnsureDirectories()
    {
        Directory.CreateDirectory(RootPath);
        Directory.CreateDirectory(ServersPath);
        Directory.CreateDirectory(CachePath);
    }

    private static void MigrateLegacyData(string legacyRootPath, string currentRootPath)
    {
        if (!Directory.Exists(legacyRootPath) || Directory.Exists(currentRootPath))
        {
            return;
        }

        try
        {
            Directory.Move(legacyRootPath, currentRootPath);
        }
        catch (IOException)
        {
            CopyDirectory(legacyRootPath, currentRootPath);
        }
        catch (UnauthorizedAccessException)
        {
            CopyDirectory(legacyRootPath, currentRootPath);
        }
    }

    private static void CopyDirectory(string sourcePath, string destinationPath)
    {
        Directory.CreateDirectory(destinationPath);

        foreach (var filePath in Directory.GetFiles(sourcePath))
        {
            var destinationFilePath = Path.Combine(destinationPath, Path.GetFileName(filePath));
            if (!File.Exists(destinationFilePath))
            {
                File.Copy(filePath, destinationFilePath);
            }
        }

        foreach (var directoryPath in Directory.GetDirectories(sourcePath))
        {
            var destinationDirectoryPath = Path.Combine(destinationPath, Path.GetFileName(directoryPath));
            CopyDirectory(directoryPath, destinationDirectoryPath);
        }
    }
}
