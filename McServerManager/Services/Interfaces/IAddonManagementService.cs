using McServerManager.Models;

namespace McServerManager.Services;

public interface IAddonManagementService
{
    bool Supports(string serverType);
    string GetCategoryName(string serverType);
    string GetActiveDirectoryPath(string serverDirectory, string serverType);
    string GetDisabledDirectoryPath(string serverDirectory, string serverType);
    IReadOnlyList<AddonEntry> GetAddons(string serverDirectory, string serverType);
    int AddFiles(string serverDirectory, string serverType, IEnumerable<string> sourcePaths);
    void Disable(string serverDirectory, string serverType, AddonEntry addon);
    void Enable(string serverDirectory, string serverType, AddonEntry addon);
    void Delete(AddonEntry addon);
    IReadOnlyList<string> AnalyzeCompatibilityWarnings(string serverType, IEnumerable<AddonEntry> addons);
    AddonImportAssessment AssessImport(string serverType, IEnumerable<string> sourcePaths, IEnumerable<AddonEntry> existingAddons);
}
