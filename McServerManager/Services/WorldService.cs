namespace McServerManager.Services;

public sealed class WorldService
{
    public IReadOnlyList<string> GetWorlds(string serverDirectory)
    {
        if (!Directory.Exists(serverDirectory))
        {
            return Array.Empty<string>();
        }

        var excluded = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "logs", "backups", "libraries", "versions", "cache"
        };

        var worlds = new List<string>();
        foreach (var dir in Directory.EnumerateDirectories(serverDirectory))
        {
            var name = Path.GetFileName(dir);
            if (excluded.Contains(name))
            {
                continue;
            }

            var hasLevelDat = File.Exists(Path.Combine(dir, "level.dat"));
            var hasRegion = Directory.Exists(Path.Combine(dir, "region"));
            if (hasLevelDat || hasRegion)
            {
                worlds.Add(name);
            }
        }

        return worlds.OrderBy(name => name).ToList();
    }

    public void CreateWorldFolder(string serverDirectory, string worldName)
    {
        var path = Path.Combine(serverDirectory, worldName);
        if (Directory.Exists(path))
        {
            throw new InvalidOperationException("同名のワールドが既に存在します。");
        }

        Directory.CreateDirectory(path);
    }

    public void DeleteWorld(string serverDirectory, string worldName)
    {
        var path = Path.Combine(serverDirectory, worldName);
        if (Directory.Exists(path))
        {
            Directory.Delete(path, true);
        }
    }
}
