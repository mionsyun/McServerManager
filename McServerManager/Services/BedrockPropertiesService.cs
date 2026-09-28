using System.Globalization;
using System.Text.Json;
using McServerManager.Models;

namespace McServerManager.Services;

public sealed class BedrockPropertiesService : IBedrockPropertiesService
{
    private const string PropertiesFileName = "server.properties";
    private const string AllowlistFileName = "allowlist.json";
    private const int SaveRetryCount = 5;
    private const int SaveRetryDelayMs = 80;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = true
    };

    /// <summary>
    /// BDS 1.26.51 以降で追加された NetherNet 用のキー。古い BDS の server.properties にはないため、
    /// ファイルにキーがなく値も空なら追記しない（未知のキーを増やさない）。
    /// </summary>
    private static readonly HashSet<string> OptionalKeys = new(StringComparer.OrdinalIgnoreCase)
    {
        "transport",
        "server-udp-ports",
        "server-ip"
    };

    private static readonly Dictionary<string, Action<BedrockServerProperties, string>> Parsers = new(StringComparer.OrdinalIgnoreCase)
    {
        ["server-name"] = (p, v) => p.ServerName = v,
        ["gamemode"] = (p, v) => p.GameMode = v,
        ["force-gamemode"] = (p, v) => p.ForceGameMode = ParseBool(v, false),
        ["difficulty"] = (p, v) => p.Difficulty = v,
        ["allow-cheats"] = (p, v) => p.AllowCheats = ParseBool(v, false),
        ["max-players"] = (p, v) => p.MaxPlayers = ParseInt(v, 10),
        ["online-mode"] = (p, v) => p.OnlineMode = ParseBool(v, true),
        ["allow-list"] = (p, v) => p.AllowList = ParseBool(v, false),
        ["server-port"] = (p, v) => p.ServerPort = ParseInt(v, ServerEditions.BedrockDefaultPort),
        ["server-portv6"] = (p, v) => p.ServerPortV6 = ParseInt(v, ServerEditions.BedrockDefaultPortV6),
        ["transport"] = (p, v) => p.Transport = v,
        ["server-udp-ports"] = (p, v) => p.ServerUdpPorts = v,
        ["server-ip"] = (p, v) => p.ServerIp = v,
        ["enable-lan-visibility"] = (p, v) => p.EnableLanVisibility = ParseBool(v, true),
        ["view-distance"] = (p, v) => p.ViewDistance = ParseInt(v, 32),
        ["tick-distance"] = (p, v) => p.TickDistance = ParseInt(v, 4),
        ["player-idle-timeout"] = (p, v) => p.PlayerIdleTimeout = ParseInt(v, 30),
        ["level-name"] = (p, v) => p.LevelName = v,
        ["level-seed"] = (p, v) => p.LevelSeed = v,
        ["default-player-permission-level"] = (p, v) => p.DefaultPlayerPermissionLevel = v,
        ["texturepack-required"] = (p, v) => p.TexturePackRequired = ParseBool(v, false)
    };

    public BedrockServerProperties Load(string serverDirectory)
    {
        var properties = new BedrockServerProperties();
        var path = Path.Combine(serverDirectory, PropertiesFileName);
        if (!File.Exists(path))
        {
            return properties;
        }

        foreach (var line in File.ReadAllLines(path))
        {
            if (!TrySplitProperty(line, out var key, out var value))
            {
                continue;
            }

            if (Parsers.TryGetValue(key, out var parser))
            {
                parser(properties, value);
            }
        }

        return properties;
    }

    /// <summary>既存の行・コメント（BDS 同梱の説明文）を保ったまま、管理対象のキーだけ書き換える。</summary>
    public void Save(string serverDirectory, BedrockServerProperties properties)
    {
        var path = Path.Combine(serverDirectory, PropertiesFileName);
        var updates = ToDictionary(properties);
        var lines = File.Exists(path) ? File.ReadAllLines(path).ToList() : new List<string>();
        var handledKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        for (var i = 0; i < lines.Count; i++)
        {
            if (TrySplitProperty(lines[i], out var key, out _) && updates.TryGetValue(key, out var newValue))
            {
                lines[i] = $"{key}={newValue}";
                handledKeys.Add(key);
            }
        }

        foreach (var (key, value) in updates)
        {
            if (handledKeys.Contains(key) || (OptionalKeys.Contains(key) && string.IsNullOrWhiteSpace(value)))
            {
                continue;
            }

            lines.Add($"{key}={value}");
        }

        WriteWithRetry(path, () => File.WriteAllLines(path, lines));
    }

    public IReadOnlyList<BedrockAllowlistEntry> LoadAllowlist(string serverDirectory)
    {
        var path = Path.Combine(serverDirectory, AllowlistFileName);
        if (!File.Exists(path))
        {
            return Array.Empty<BedrockAllowlistEntry>();
        }

        var json = File.ReadAllText(path);
        if (string.IsNullOrWhiteSpace(json))
        {
            return Array.Empty<BedrockAllowlistEntry>();
        }

        return JsonSerializer.Deserialize<List<BedrockAllowlistEntry>>(json, JsonOptions)
               ?? new List<BedrockAllowlistEntry>();
    }

    public void SaveAllowlist(string serverDirectory, IEnumerable<BedrockAllowlistEntry> entries)
    {
        var path = Path.Combine(serverDirectory, AllowlistFileName);
        var json = JsonSerializer.Serialize(entries.ToList(), JsonOptions);
        WriteWithRetry(path, () => File.WriteAllText(path, json));
    }

    private static Dictionary<string, string> ToDictionary(BedrockServerProperties p) =>
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["server-name"] = SanitizeServerName(p.ServerName),
            ["gamemode"] = p.GameMode,
            ["force-gamemode"] = FormatBool(p.ForceGameMode),
            ["difficulty"] = p.Difficulty,
            ["allow-cheats"] = FormatBool(p.AllowCheats),
            ["max-players"] = p.MaxPlayers.ToString(CultureInfo.InvariantCulture),
            ["online-mode"] = FormatBool(p.OnlineMode),
            ["allow-list"] = FormatBool(p.AllowList),
            ["server-port"] = p.ServerPort.ToString(CultureInfo.InvariantCulture),
            ["server-portv6"] = p.ServerPortV6.ToString(CultureInfo.InvariantCulture),
            ["transport"] = p.Transport.Trim().ToLowerInvariant(),
            ["server-udp-ports"] = p.ServerUdpPorts.Trim(),
            ["server-ip"] = p.ServerIp.Trim(),
            ["enable-lan-visibility"] = FormatBool(p.EnableLanVisibility),
            ["view-distance"] = p.ViewDistance.ToString(CultureInfo.InvariantCulture),
            ["tick-distance"] = p.TickDistance.ToString(CultureInfo.InvariantCulture),
            ["player-idle-timeout"] = p.PlayerIdleTimeout.ToString(CultureInfo.InvariantCulture),
            ["level-name"] = p.LevelName,
            ["level-seed"] = p.LevelSeed,
            ["default-player-permission-level"] = p.DefaultPlayerPermissionLevel,
            ["texturepack-required"] = FormatBool(p.TexturePackRequired)
        };

    /// <summary>BDS の server-name はセミコロンを含められない（接続時の情報区切りに使われるため）。</summary>
    private static string SanitizeServerName(string? value)
    {
        var name = (value ?? string.Empty).Replace(";", string.Empty).Replace("\r", string.Empty).Replace("\n", string.Empty).Trim();
        return string.IsNullOrWhiteSpace(name) ? "Dedicated Server" : name;
    }

    private static bool TrySplitProperty(string line, out string key, out string value)
    {
        key = string.Empty;
        value = string.Empty;
        var trimmed = line.Trim();
        if (string.IsNullOrWhiteSpace(trimmed) || trimmed.StartsWith('#'))
        {
            return false;
        }

        var separatorIndex = trimmed.IndexOf('=');
        if (separatorIndex <= 0)
        {
            return false;
        }

        key = trimmed[..separatorIndex].Trim();
        value = trimmed[(separatorIndex + 1)..].Trim();
        return true;
    }

    private static void WriteWithRetry(string path, Action write)
    {
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                write();
                return;
            }
            catch (IOException) when (attempt < SaveRetryCount)
            {
                // 起動中の BDS やウイルス対策ソフトが一時的にファイルを掴んでいる場合がある
                Thread.Sleep(SaveRetryDelayMs * attempt);
            }
        }
    }

    private static string FormatBool(bool value) => value ? "true" : "false";

    private static int ParseInt(string value, int fallback) =>
        int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed) ? parsed : fallback;

    private static bool ParseBool(string value, bool fallback) =>
        bool.TryParse(value, out var parsed) ? parsed : fallback;
}
