using McServerManager.Models;

namespace McServerManager.Services;

public sealed class ServerPropertiesService
{
    private static readonly Dictionary<string, Action<ServerProperties, string>> Parsers = new(StringComparer.OrdinalIgnoreCase)
    {
        ["server-port"] = (p, v) => p.ServerPort = ParseInt(v, 25565),
        ["max-players"] = (p, v) => p.MaxPlayers = ParseInt(v, 20),
        ["motd"] = (p, v) => p.Motd = v,
        ["online-mode"] = (p, v) => p.OnlineMode = ParseBool(v, true),
        ["enable-command-block"] = (p, v) => p.EnableCommandBlock = ParseBool(v, false),
        ["difficulty"] = (p, v) => p.Difficulty = v,
        ["gamemode"] = (p, v) => p.GameMode = v,
        ["pvp"] = (p, v) => p.Pvp = ParseBool(v, true),
        ["view-distance"] = (p, v) => p.ViewDistance = ParseInt(v, 10),
        ["spawn-protection"] = (p, v) => p.SpawnProtection = ParseInt(v, 16),
        ["level-name"] = (p, v) => p.LevelName = v,
        ["level-seed"] = (p, v) => p.Seed = v
    };

    public ServerProperties Load(string serverDirectory, ServerConfig fallback)
    {
        var properties = new ServerProperties
        {
            ServerPort = fallback.Port,
            MaxPlayers = fallback.MaxPlayers,
            Motd = fallback.Motd,
            OnlineMode = fallback.OnlineMode,
            EnableCommandBlock = fallback.EnableCommandBlock,
            Difficulty = fallback.Difficulty,
            GameMode = fallback.GameMode,
            Pvp = fallback.Pvp,
            ViewDistance = fallback.ViewDistance,
            SpawnProtection = fallback.SpawnProtection,
            LevelName = fallback.WorldName,
            Seed = fallback.Seed
        };

        var path = Path.Combine(serverDirectory, "server.properties");
        if (!File.Exists(path))
        {
            return properties;
        }

        foreach (var line in File.ReadAllLines(path))
        {
            var trimmed = line.Trim();
            if (string.IsNullOrWhiteSpace(trimmed) || trimmed.StartsWith('#'))
            {
                continue;
            }

            var separatorIndex = trimmed.IndexOf('=');
            if (separatorIndex <= 0)
            {
                continue;
            }

            var key = trimmed[..separatorIndex].Trim();
            var value = trimmed[(separatorIndex + 1)..].Trim();
            if (Parsers.TryGetValue(key, out var parser))
            {
                parser(properties, value);
            }
        }

        return properties;
    }

    public void Save(string serverDirectory, ServerProperties properties)
    {
        var path = Path.Combine(serverDirectory, "server.properties");
        var updates = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["server-port"] = properties.ServerPort.ToString(),
            ["max-players"] = properties.MaxPlayers.ToString(),
            ["motd"] = properties.Motd,
            ["online-mode"] = properties.OnlineMode ? "true" : "false",
            ["enable-command-block"] = properties.EnableCommandBlock ? "true" : "false",
            ["difficulty"] = properties.Difficulty,
            ["gamemode"] = properties.GameMode,
            ["pvp"] = properties.Pvp ? "true" : "false",
            ["view-distance"] = properties.ViewDistance.ToString(),
            ["spawn-protection"] = properties.SpawnProtection.ToString(),
            ["level-name"] = properties.LevelName,
            ["level-seed"] = properties.Seed
        };

        var lines = File.Exists(path) ? File.ReadAllLines(path).ToList() : new List<string>();
        var handledKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        for (var i = 0; i < lines.Count; i++)
        {
            var line = lines[i];
            var trimmed = line.Trim();
            if (string.IsNullOrWhiteSpace(trimmed) || trimmed.StartsWith('#'))
            {
                continue;
            }

            var separatorIndex = trimmed.IndexOf('=');
            if (separatorIndex <= 0)
            {
                continue;
            }

            var key = trimmed[..separatorIndex].Trim();
            if (updates.TryGetValue(key, out var newValue))
            {
                lines[i] = $"{key}={newValue}";
                handledKeys.Add(key);
            }
        }

        foreach (var kvp in updates)
        {
            if (!handledKeys.Contains(kvp.Key))
            {
                lines.Add($"{kvp.Key}={kvp.Value}");
            }
        }

        File.WriteAllLines(path, lines);
    }

    private static int ParseInt(string value, int fallback)
    {
        return int.TryParse(value, out var parsed) ? parsed : fallback;
    }

    private static bool ParseBool(string value, bool fallback)
    {
        return bool.TryParse(value, out var parsed) ? parsed : fallback;
    }
}
