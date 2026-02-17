using System.Net.Http;
using System.Text.Json;
using McServerManager.Models;

namespace McServerManager.Services;

public sealed class AddonCatalogService
{
    private const string SearchApi = "https://api.modrinth.com/v2/search";
    private readonly HttpClient _httpClient = new();

    public async Task<IReadOnlyList<AddonSearchResult>> SearchAsync(string query, string serverType, string minecraftVersion, int limit = 15)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            return Array.Empty<AddonSearchResult>();
        }

        var encodedQuery = Uri.EscapeDataString(query.Trim());
        var cappedLimit = Math.Clamp(limit, 1, 30);
        var requestUrl = $"{SearchApi}?query={encodedQuery}&limit={cappedLimit}&index=relevance";

        using var response = await _httpClient.GetAsync(requestUrl).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        var json = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
        using var doc = JsonDocument.Parse(json);

        if (!doc.RootElement.TryGetProperty("hits", out var hits) || hits.ValueKind != JsonValueKind.Array)
        {
            return Array.Empty<AddonSearchResult>();
        }

        var expectedProjectType = ResolveProjectType(serverType);
        var results = new List<AddonSearchResult>();

        foreach (var hit in hits.EnumerateArray())
        {
            var projectType = hit.TryGetProperty("project_type", out var projectTypeElement)
                ? projectTypeElement.GetString() ?? string.Empty
                : string.Empty;
            if (!string.Equals(projectType, expectedProjectType, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (!IsCompatibleVersion(hit, minecraftVersion))
            {
                continue;
            }

            var slug = hit.TryGetProperty("slug", out var slugElement) ? slugElement.GetString() ?? string.Empty : string.Empty;
            var projectId = hit.TryGetProperty("project_id", out var idElement) ? idElement.GetString() ?? string.Empty : string.Empty;
            var title = hit.TryGetProperty("title", out var titleElement) ? titleElement.GetString() ?? slug : slug;
            var description = hit.TryGetProperty("description", out var descriptionElement)
                ? descriptionElement.GetString() ?? string.Empty
                : string.Empty;
            var downloads = hit.TryGetProperty("downloads", out var downloadsElement)
                ? downloadsElement.GetInt32()
                : 0;

            if (string.IsNullOrWhiteSpace(slug))
            {
                continue;
            }

            results.Add(new AddonSearchResult
            {
                ProjectId = projectId,
                Slug = slug,
                Title = title,
                Description = description,
                ProjectType = projectType,
                DownloadCount = downloads
            });
        }

        return results
            .OrderByDescending(item => item.DownloadCount)
            .ThenBy(item => item.Title, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static string ResolveProjectType(string serverType)
    {
        if (string.Equals(serverType, "Forge", StringComparison.OrdinalIgnoreCase)
            || string.Equals(serverType, "Fabric", StringComparison.OrdinalIgnoreCase))
        {
            return "mod";
        }

        return "plugin";
    }

    private static bool IsCompatibleVersion(JsonElement hit, string minecraftVersion)
    {
        if (string.IsNullOrWhiteSpace(minecraftVersion))
        {
            return true;
        }

        if (!hit.TryGetProperty("versions", out var versionsElement) || versionsElement.ValueKind != JsonValueKind.Array)
        {
            return true;
        }

        foreach (var versionElement in versionsElement.EnumerateArray())
        {
            var value = versionElement.GetString();
            if (string.Equals(value, minecraftVersion, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }
}
