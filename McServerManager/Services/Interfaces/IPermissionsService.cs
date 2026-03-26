using McServerManager.Models;

namespace McServerManager.Services;

public interface IPermissionsService
{
    List<OpEntry> LoadOps(string serverDirectory);
    void SaveOps(string serverDirectory, IEnumerable<OpEntry> entries);
    List<WhitelistEntry> LoadWhitelist(string serverDirectory);
    void SaveWhitelist(string serverDirectory, IEnumerable<WhitelistEntry> entries);
}
