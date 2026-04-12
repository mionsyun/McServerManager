using System.Net.Http;
using System.Text.Json;
using McServerManager.Models;

namespace McServerManager.Services;

public sealed class AddonCatalogService : IAddonCatalogService
{
    private const string SearchApi = ExternalApiUrls.ModrinthSearch;
    private readonly HttpClient _httpClient;

    public AddonCatalogService(HttpClient? httpClient = null)
    {
        _httpClient = httpClient ?? new HttpClient();
        _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("McServerManager/1.0 (shunki@github)");
    }

    public async Task<IReadOnlyList<AddonSearchResult>> SearchAsync(string query, string serverType, string minecraftVersion, int limit = 20)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            return Array.Empty<AddonSearchResult>();
        }

        var encodedQuery = Uri.EscapeDataString(query.Trim());
        var cappedLimit = Math.Clamp(limit, 1, 100);

        // Modrinth facets: サーバー側でフィルタリングして精度を上げる
        var facets = BuildFacets(serverType, minecraftVersion);
        var facetsParam = string.IsNullOrEmpty(facets) ? string.Empty : $"&facets={Uri.EscapeDataString(facets)}";
        var requestUrl = $"{SearchApi}?query={encodedQuery}&limit={cappedLimit}&index=relevance{facetsParam}";

        using var response = await _httpClient.GetAsync(requestUrl).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        var json = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
        using var doc = JsonDocument.Parse(json);

        if (!doc.RootElement.TryGetProperty("hits", out var hits) || hits.ValueKind != JsonValueKind.Array)
        {
            return Array.Empty<AddonSearchResult>();
        }

        var results = new List<AddonSearchResult>();

        foreach (var hit in hits.EnumerateArray())
        {
            var slug = hit.TryGetProperty("slug", out var slugElement) ? slugElement.GetString() ?? string.Empty : string.Empty;
            var projectId = hit.TryGetProperty("project_id", out var idElement) ? idElement.GetString() ?? string.Empty : string.Empty;
            var title = hit.TryGetProperty("title", out var titleElement) ? titleElement.GetString() ?? slug : slug;
            var description = hit.TryGetProperty("description", out var descriptionElement)
                ? descriptionElement.GetString() ?? string.Empty
                : string.Empty;
            var downloads = hit.TryGetProperty("downloads", out var downloadsElement)
                ? downloadsElement.GetInt32()
                : 0;
            var iconUrl = hit.TryGetProperty("icon_url", out var iconElement)
                ? iconElement.GetString() ?? string.Empty
                : string.Empty;
            var projectType = hit.TryGetProperty("project_type", out var ptElement)
                ? ptElement.GetString() ?? string.Empty
                : string.Empty;

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

    private static string BuildFacets(string serverType, string minecraftVersion)
    {
        // Modrinth facets 形式: [["key:value"],["key:value"]] (AND条件)
        // 同一配列内は OR 条件: [["categories:forge","categories:neoforge"]]
        var groups = new List<string>();

        // project_type フィルター
        var projectType = ResolveProjectType(serverType);
        if (!string.IsNullOrEmpty(projectType))
        {
            groups.Add($"[\"project_type:{projectType}\"]");
        }

        // loader/categories フィルター
        var loaderFacet = ResolveLoaderFacet(serverType);
        if (!string.IsNullOrEmpty(loaderFacet))
        {
            groups.Add(loaderFacet);
        }

        // バージョンフィルター
        if (!string.IsNullOrWhiteSpace(minecraftVersion))
        {
            groups.Add($"[\"versions:{minecraftVersion}\"]");
        }

        if (groups.Count == 0)
        {
            return string.Empty;
        }

        return $"[{string.Join(",", groups)}]";
    }

    private static string ResolveProjectType(string serverType)
    {
        if (string.Equals(serverType, "Forge", StringComparison.OrdinalIgnoreCase)
            || string.Equals(serverType, "Fabric", StringComparison.OrdinalIgnoreCase))
        {
            return "mod";
        }

        if (string.Equals(serverType, "Paper", StringComparison.OrdinalIgnoreCase)
            || string.Equals(serverType, "Spigot", StringComparison.OrdinalIgnoreCase)
            || string.Equals(serverType, "Purpur", StringComparison.OrdinalIgnoreCase))
        {
            return "plugin";
        }

        return string.Empty;
    }

    private static string ResolveLoaderFacet(string serverType)
    {
        return serverType.ToLowerInvariant() switch
        {
            "forge" => "[\"categories:forge\",\"categories:neoforge\"]",
            "fabric" => "[\"categories:fabric\"]",
            "paper" => "[\"categories:paper\",\"categories:bukkit\"]",
            "spigot" => "[\"categories:spigot\",\"categories:bukkit\"]",
            "purpur" => "[\"categories:purpur\",\"categories:paper\",\"categories:bukkit\"]",
            _ => string.Empty
        };
    }
}
