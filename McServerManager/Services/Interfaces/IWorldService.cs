using McServerManager.Models;

namespace McServerManager.Services;

public interface IWorldService
{
    IReadOnlyList<string> GetWorlds(string serverDirectory);
    string GetBackupsDirectory(string serverDirectory);
    IReadOnlyList<WorldBackupEntry> GetWorldBackups(string serverDirectory);
    WorldBackupEntry CreateWorldBackup(string serverDirectory, string worldName);
    WorldBackupEntry CreateAutomaticWorldBackup(string serverDirectory, string worldName, int maxRetention);
    string RestoreWorldBackup(string serverDirectory, string backupZipPath, string targetWorldName, bool overwriteExisting, bool createBackupBeforeRestore);
    void DeleteWorldBackup(string backupZipPath);
    void CreateWorldFolder(string serverDirectory, string worldName);
    void DeleteWorld(string serverDirectory, string worldName);
    IReadOnlyList<ArchiveWorldCandidate> GetArchiveWorldCandidates(string archivePath);
    string ImportWorldArchive(string serverDirectory, string archivePath, string targetWorldName, bool overwriteExisting, string? sourceWorldRelativePath = null);
}
