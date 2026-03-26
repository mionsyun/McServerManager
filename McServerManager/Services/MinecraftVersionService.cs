using System.Net.Http;
using System.Text.Json;
using McServerManager.Models;

namespace McServerManager.Services;

public sealed class MinecraftVersionService : IMinecraftVersionService
{
    private const string ManifestUrl = "https://launchermeta.mojang.com/mc/game/version_manifest.json";
    private static readonly TimeSpan ManifestCacheMaxAge = TimeSpan.FromHours(6);
    private readonly AppPathsService _pathsService;
    private readonly HttpClient _httpClient = new();

    public MinecraftVersionService(AppPathsService pathsService)
    {
        _pathsService = pathsService;
    }

    public async Task<IReadOnlyList<MinecraftVersionInfo>> GetVersionsAsync(bool forceRefresh = false)
    {
        var cachePath = Path.Combine(_pathsService.CachePath, "version_manifest.json");
        Directory.CreateDirectory(_pathsService.CachePath);

        string? cachedJson = null;
        if (File.Exists(cachePath))
        {
            cachedJson = await File.ReadAllTextAsync(cachePath).ConfigureAwait(false);
            if (!forceRefresh && !IsCacheExpired(cachePath))
            {
                var cached = TryParseVersions(cachedJson);
                if (cached is not null)
                {
                    return cached;
                }
            }
        }

        try
        {
            var json = await _httpClient.GetStringAsync(ManifestUrl).ConfigureAwait(false);
            await File.WriteAllTextAsync(cachePath, json).ConfigureAwait(false);

            var fetched = TryParseVersions(json);
            if (fetched is not null)
            {
                return fetched;
            }

            throw new InvalidOperationException("バージョンマニフェストの解析に失敗しました。");
        }
        catch when (!string.IsNullOrWhiteSpace(cachedJson))
        {
            var fallback = TryParseVersions(cachedJson);
            if (fallback is not null)
            {
                return fallback;
            }

            throw;
        }
    }

    private static bool IsCacheExpired(string cachePath)
    {
        var updatedAtUtc = File.GetLastWriteTimeUtc(cachePath);
        if (updatedAtUtc == DateTime.MinValue)
        {
            return true;
        }

        return DateTime.UtcNow - updatedAtUtc > ManifestCacheMaxAge;
    }

    private static List<MinecraftVersionInfo>? TryParseVersions(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return null;
        }

        using var doc = JsonDocument.Parse(json);
        if (!doc.RootElement.TryGetProperty("versions", out var versionsElement))
        {
            return null;
        }

        var list = new List<MinecraftVersionInfo>();
        foreach (var element in versionsElement.EnumerateArray())
        {
            var id = element.GetProperty("id").GetString() ?? string.Empty;
            var type = element.GetProperty("type").GetString() ?? string.Empty;
            var url = element.GetProperty("url").GetString() ?? string.Empty;
            var releaseTime = element.GetProperty("releaseTime").GetDateTime();
            list.Add(new MinecraftVersionInfo
            {
                Id = id,
                Type = type,
                ManifestUrl = url,
                ReleaseTime = releaseTime
            });
        }

        return list;
    }

    public async Task DownloadServerJarAsync(string versionId, string destinationPath)
    {
        var versionManifestUrl = await GetVersionManifestUrlAsync(versionId).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(versionManifestUrl))
        {
            throw new InvalidOperationException("バージョン情報が取得できませんでした。");
        }

        var serverJarUrl = await GetServerJarUrlAsync(versionManifestUrl).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(serverJarUrl))
        {
            throw new InvalidOperationException("server.jar のURLが取得できませんでした。");
        }

        using var response = await _httpClient.GetAsync(serverJarUrl).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        var directory = Path.GetDirectoryName(destinationPath);
        if (string.IsNullOrWhiteSpace(directory))
        {
            throw new InvalidOperationException("保存先フォルダを特定できませんでした。");
        }

        Directory.CreateDirectory(directory);
        await using var output = File.Create(destinationPath);
        await response.Content.CopyToAsync(output).ConfigureAwait(false);
    }

    private async Task<string?> GetVersionManifestUrlAsync(string versionId)
    {
        var versions = await GetVersionsAsync().ConfigureAwait(false);
        var match = versions.FirstOrDefault(v => string.Equals(v.Id, versionId, StringComparison.OrdinalIgnoreCase));
        if (match is null)
        {
            versions = await GetVersionsAsync(forceRefresh: true).ConfigureAwait(false);
            match = versions.FirstOrDefault(v => string.Equals(v.Id, versionId, StringComparison.OrdinalIgnoreCase));
        }

        return match?.ManifestUrl;
    }

    private async Task<string?> GetServerJarUrlAsync(string versionManifestUrl)
    {
        var json = await _httpClient.GetStringAsync(versionManifestUrl).ConfigureAwait(false);
        using var doc = JsonDocument.Parse(json);
        if (!doc.RootElement.TryGetProperty("downloads", out var downloadsElement))
        {
            return null;
        }

        if (!downloadsElement.TryGetProperty("server", out var serverElement))
        {
            return null;
        }

        return serverElement.GetProperty("url").GetString();
    }
}
