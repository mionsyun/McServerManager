using System.Net.Http;
using System.Text.Json;
using McServerManager.Models;

namespace McServerManager.Services;

public sealed class MinecraftVersionService
{
    private const string ManifestUrl = "https://launchermeta.mojang.com/mc/game/version_manifest.json";
    private readonly AppPathsService _pathsService;
    private readonly HttpClient _httpClient = new();

    public MinecraftVersionService(AppPathsService pathsService)
    {
        _pathsService = pathsService;
    }

    public async Task<IReadOnlyList<MinecraftVersionInfo>> GetVersionsAsync(bool forceRefresh = false)
    {
        var cachePath = Path.Combine(_pathsService.CachePath, "version_manifest.json");
        string json;

        if (!forceRefresh && File.Exists(cachePath))
        {
            json = await File.ReadAllTextAsync(cachePath).ConfigureAwait(false);
        }
        else
        {
            Directory.CreateDirectory(_pathsService.CachePath);
            json = await _httpClient.GetStringAsync(ManifestUrl).ConfigureAwait(false);
            await File.WriteAllTextAsync(cachePath, json).ConfigureAwait(false);
        }

        using var doc = JsonDocument.Parse(json);
        var list = new List<MinecraftVersionInfo>();
        if (!doc.RootElement.TryGetProperty("versions", out var versionsElement))
        {
            return list;
        }

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

        await using var output = File.Create(destinationPath);
        await response.Content.CopyToAsync(output).ConfigureAwait(false);
    }

    private async Task<string?> GetVersionManifestUrlAsync(string versionId)
    {
        var versions = await GetVersionsAsync().ConfigureAwait(false);
        var match = versions.FirstOrDefault(v => string.Equals(v.Id, versionId, StringComparison.OrdinalIgnoreCase));
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
