using System.Text.Json;
using McServerManager.Models;

namespace McServerManager.Services;

public sealed class PermissionsService : IPermissionsService
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = true
    };

    public List<OpEntry> LoadOps(string serverDirectory)
    {
        var path = Path.Combine(serverDirectory, "ops.json");
        return Load<List<OpEntry>>(path) ?? new List<OpEntry>();
    }

    public void SaveOps(string serverDirectory, IEnumerable<OpEntry> entries)
    {
        var path = Path.Combine(serverDirectory, "ops.json");
        Save(path, entries);
    }

    public List<WhitelistEntry> LoadWhitelist(string serverDirectory)
    {
        var path = Path.Combine(serverDirectory, "whitelist.json");
        return Load<List<WhitelistEntry>>(path) ?? new List<WhitelistEntry>();
    }

    public void SaveWhitelist(string serverDirectory, IEnumerable<WhitelistEntry> entries)
    {
        var path = Path.Combine(serverDirectory, "whitelist.json");
        Save(path, entries);
    }

    private static T? Load<T>(string path)
    {
        try
        {
            if (!File.Exists(path))
            {
                return default;
            }

            var json = File.ReadAllText(path);
            return JsonSerializer.Deserialize<T>(json, Options);
        }
        catch
        {
            return default;
        }
    }

    private static void Save<T>(string path, T data)
    {
        var json = JsonSerializer.Serialize(data, Options);
        File.WriteAllText(path, json);
    }
}
