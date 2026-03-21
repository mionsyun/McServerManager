using System.Text.Json;
using McServerManager.Models;

namespace McServerManager.Services;

public sealed class ServerConfigService
{
    private static readonly object SaveLock = new();
    private const int SaveRetryCount = 5;
    private readonly AppPathsService _pathsService;
    private readonly JsonSerializerOptions _options = new() { WriteIndented = true };

    public ServerConfigService(AppPathsService pathsService)
    {
        _pathsService = pathsService;
    }

    public IReadOnlyList<ServerConfig> LoadAll(IEnumerable<string>? additionalDirectories = null)
    {
        var byServerId = new Dictionary<string, (ServerConfig Config, DateTime ConfigUpdatedAtUtc)>(StringComparer.OrdinalIgnoreCase);
        var seenConfigPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var searchRoots = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            _pathsService.ServersPath
        };

        if (additionalDirectories is not null)
        {
            foreach (var dir in additionalDirectories)
            {
                if (!string.IsNullOrWhiteSpace(dir))
                {
                    searchRoots.Add(dir);
                }
            }
        }

        foreach (var root in searchRoots)
        {
            if (!Directory.Exists(root))
            {
                continue;
            }

            foreach (var configPath in EnumerateConfigPaths(root))
            {
                try
                {
                    var fullConfigPath = Path.GetFullPath(configPath);
                    if (!seenConfigPaths.Add(fullConfigPath))
                    {
                        continue;
                    }

                    var json = File.ReadAllText(configPath);
                    var config = JsonSerializer.Deserialize<ServerConfig>(json);
                    if (config is null || string.IsNullOrWhiteSpace(config.ServerId))
                    {
                        continue;
                    }
                    if (string.IsNullOrWhiteSpace(config.DirectoryPath))
                    {
                        config.DirectoryPath = Path.GetDirectoryName(configPath) ?? string.Empty;
                    }

                    var updatedAtUtc = File.GetLastWriteTimeUtc(configPath);
                    if (byServerId.TryGetValue(config.ServerId, out var existing)
                        && existing.ConfigUpdatedAtUtc >= updatedAtUtc)
                    {
                        continue;
                    }

                    byServerId[config.ServerId] = (config, updatedAtUtc);
                }
                catch
                {
                    // Skip invalid configs
                }
            }
        }

        return byServerId.Values
            .Select(item => item.Config)
            .ToList();
    }

    public ServerConfig? Load(string serverId)
    {
        var path = _pathsService.GetServerConfigPath(serverId);
        if (!File.Exists(path))
        {
            return null;
        }

        try
        {
            var json = File.ReadAllText(path);
            var config = JsonSerializer.Deserialize<ServerConfig>(json);
            if (config is not null && string.IsNullOrWhiteSpace(config.DirectoryPath))
            {
                config.DirectoryPath = Path.GetDirectoryName(path) ?? string.Empty;
            }
            return config;
        }
        catch
        {
            return null;
        }
    }

    public void Save(ServerConfig config)
    {
        if (!string.IsNullOrWhiteSpace(config.DirectoryPath))
        {
            SaveToDirectory(config, config.DirectoryPath);
            return;
        }

        SaveToPath(config, _pathsService.GetServerConfigPath(config.ServerId));
    }

    public void SaveToDirectory(ServerConfig config, string serverDirectory)
    {
        var path = Path.Combine(serverDirectory, "config.json");
        SaveToPath(config, path);
    }

    private void SaveToPath(ServerConfig config, string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path) ?? _pathsService.ServersPath);
        var json = JsonSerializer.Serialize(config, _options);

        IOException? lastIoException = null;
        for (var attempt = 0; attempt < SaveRetryCount; attempt++)
        {
            try
            {
                lock (SaveLock)
                {
                    File.WriteAllText(path, json);
                }
                return;
            }
            catch (IOException ex) when (IsSharingViolation(ex) && attempt < SaveRetryCount - 1)
            {
                lastIoException = ex;
                var delayMs = 25 * (1 << attempt);
                Thread.Sleep(delayMs);
            }
        }

        if (lastIoException is not null)
        {
            throw lastIoException;
        }

        File.WriteAllText(path, json);
    }

    private static bool IsSharingViolation(IOException ex)
    {
        const int sharingViolation = 32;
        const int lockViolation = 33;
        var errorCode = ex.HResult & 0xFFFF;
        return errorCode == sharingViolation || errorCode == lockViolation;
    }

    private static IEnumerable<string> EnumerateConfigPaths(string root)
    {
        var rootConfig = Path.Combine(root, "config.json");
        if (File.Exists(rootConfig))
        {
            yield return rootConfig;
        }

        IEnumerable<string> childDirectories;
        try
        {
            childDirectories = Directory.EnumerateDirectories(root);
        }
        catch
        {
            yield break;
        }

        foreach (var directory in childDirectories)
        {
            var candidate = Path.Combine(directory, "config.json");
            if (File.Exists(candidate))
            {
                yield return candidate;
            }
        }
    }
}
