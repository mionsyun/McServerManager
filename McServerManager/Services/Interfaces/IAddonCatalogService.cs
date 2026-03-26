using McServerManager.Models;

namespace McServerManager.Services;

public interface IAddonCatalogService
{
    Task<IReadOnlyList<AddonSearchResult>> SearchAsync(string query, string serverType, string minecraftVersion, int limit = 20);
}
