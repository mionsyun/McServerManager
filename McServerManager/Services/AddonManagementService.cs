using McServerManager.Models;
using System.Text.RegularExpressions;

namespace McServerManager.Services;

public sealed class AddonManagementService
{
    private static readonly HashSet<string> PluginServerTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "Paper", "Purpur", "Spigot"
    };

    private static readonly HashSet<string> ModServerTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "Forge", "Fabric"
    };

    private static readonly Regex VersionSuffixRegex = new(@"([\-_.]?v?\d+[\w\-\.]*)$", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public bool Supports(string serverType)
        => PluginServerTypes.Contains(serverType) || ModServerTypes.Contains(serverType);

    public string GetCategoryName(string serverType)
    {
        if (PluginServerTypes.Contains(serverType))
        {
            return "プラグイン";
        }

        if (ModServerTypes.Contains(serverType))
        {
            return "MOD";
        }

        return "対象外";
    }

    public string GetActiveDirectoryPath(string serverDirectory, string serverType)
    {
        return Path.Combine(serverDirectory, GetRootDirectoryName(serverType));
    }

    public string GetDisabledDirectoryPath(string serverDirectory, string serverType)
    {
        return Path.Combine(GetActiveDirectoryPath(serverDirectory, serverType), "disabled");
    }

    public IReadOnlyList<AddonEntry> GetAddons(string serverDirectory, string serverType)
    {
        EnsureSupported(serverType);

        var activeDirectory = GetActiveDirectoryPath(serverDirectory, serverType);
        var disabledDirectory = GetDisabledDirectoryPath(serverDirectory, serverType);
        Directory.CreateDirectory(activeDirectory);
        Directory.CreateDirectory(disabledDirectory);

        var entries = new List<AddonEntry>();
        entries.AddRange(ReadEntries(activeDirectory, true));
        entries.AddRange(ReadEntries(disabledDirectory, false));

        return entries
            .OrderByDescending(item => item.IsEnabled)
            .ThenBy(item => item.FileName, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public int AddFiles(string serverDirectory, string serverType, IEnumerable<string> sourcePaths)
    {
        EnsureSupported(serverType);

        var activeDirectory = GetActiveDirectoryPath(serverDirectory, serverType);
        Directory.CreateDirectory(activeDirectory);

        var copiedCount = 0;
        foreach (var filePath in ExpandJarFiles(sourcePaths))
        {
            var fileName = Path.GetFileName(filePath);
            var destinationPath = EnsureUniqueDestinationPath(activeDirectory, fileName);
            File.Copy(filePath, destinationPath, false);
            copiedCount++;
        }

        return copiedCount;
    }

    public void Disable(string serverDirectory, string serverType, AddonEntry addon)
    {
        EnsureSupported(serverType);
        if (!addon.IsEnabled)
        {
            return;
        }

        var disabledDirectory = GetDisabledDirectoryPath(serverDirectory, serverType);
        Directory.CreateDirectory(disabledDirectory);
        MoveWithUniqueName(addon.FullPath, disabledDirectory);
    }

    public void Enable(string serverDirectory, string serverType, AddonEntry addon)
    {
        EnsureSupported(serverType);
        if (addon.IsEnabled)
        {
            return;
        }

        var activeDirectory = GetActiveDirectoryPath(serverDirectory, serverType);
        Directory.CreateDirectory(activeDirectory);
        MoveWithUniqueName(addon.FullPath, activeDirectory);
    }

    public void Delete(AddonEntry addon)
    {
        if (File.Exists(addon.FullPath))
        {
            File.Delete(addon.FullPath);
        }
    }

    public IReadOnlyList<string> AnalyzeCompatibilityWarnings(string serverType, IEnumerable<AddonEntry> addons)
    {
        EnsureSupported(serverType);

        var warnings = new List<string>();
        var addonList = addons.ToList();

        var duplicateGroups = addonList
            .GroupBy(addon => NormalizeAddonKey(addon.FileName), StringComparer.OrdinalIgnoreCase)
            .Where(group => !string.IsNullOrWhiteSpace(group.Key) && group.Count() > 1)
            .ToList();

        foreach (var group in duplicateGroups)
        {
            var names = string.Join(", ", group.Select(item => item.FileName));
            warnings.Add($"重複の可能性: {names}");
        }

        if (string.Equals(serverType, "Fabric", StringComparison.OrdinalIgnoreCase))
        {
            foreach (var addon in addonList)
            {
                var name = addon.FileName;
                if (ContainsAny(name, "forge", "neoforge"))
                {
                    warnings.Add($"FabricサーバーでForge系の可能性: {name}");
                }
            }
        }

        if (string.Equals(serverType, "Forge", StringComparison.OrdinalIgnoreCase))
        {
            foreach (var addon in addonList)
            {
                var name = addon.FileName;
                if (ContainsAny(name, "fabric", "quilt"))
                {
                    warnings.Add($"ForgeサーバーでFabric/Quilt系の可能性: {name}");
                }
            }
        }

        if (PluginServerTypes.Contains(serverType))
        {
            foreach (var addon in addonList)
            {
                var name = addon.FileName;
                if (ContainsAny(name, "fabric", "forge", "neoforge", "quilt", "mod"))
                {
                    warnings.Add($"プラグインサーバーでMOD系の可能性: {name}");
                }
            }
        }

        return warnings.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }

    private static IEnumerable<AddonEntry> ReadEntries(string directoryPath, bool isEnabled)
    {
        if (!Directory.Exists(directoryPath))
        {
            yield break;
        }

        foreach (var filePath in Directory.EnumerateFiles(directoryPath, "*.jar", SearchOption.TopDirectoryOnly))
        {
            var info = new FileInfo(filePath);
            yield return new AddonEntry
            {
                FileName = info.Name,
                FullPath = info.FullName,
                IsEnabled = isEnabled,
                FileSizeBytes = info.Exists ? info.Length : 0,
                LastUpdatedAt = info.Exists ? info.LastWriteTime : DateTime.MinValue
            };
        }
    }

    private static IEnumerable<string> ExpandJarFiles(IEnumerable<string> sourcePaths)
    {
        foreach (var sourcePath in sourcePaths.Where(path => !string.IsNullOrWhiteSpace(path)).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (File.Exists(sourcePath) && string.Equals(Path.GetExtension(sourcePath), ".jar", StringComparison.OrdinalIgnoreCase))
            {
                yield return sourcePath;
                continue;
            }

            if (Directory.Exists(sourcePath))
            {
                foreach (var filePath in Directory.EnumerateFiles(sourcePath, "*.jar", SearchOption.AllDirectories))
                {
                    yield return filePath;
                }
            }
        }
    }

    private static string NormalizeAddonKey(string fileName)
    {
        var stem = Path.GetFileNameWithoutExtension(fileName);
        stem = VersionSuffixRegex.Replace(stem, string.Empty);
        stem = stem.Trim('-', '_', '.', ' ');
        return stem;
    }

    private static bool ContainsAny(string value, params string[] keywords)
        => keywords.Any(keyword => value.Contains(keyword, StringComparison.OrdinalIgnoreCase));

    private static string EnsureUniqueDestinationPath(string destinationDirectory, string fileName)
    {
        var candidatePath = Path.Combine(destinationDirectory, fileName);
        if (!File.Exists(candidatePath))
        {
            return candidatePath;
        }

        var stem = Path.GetFileNameWithoutExtension(fileName);
        var ext = Path.GetExtension(fileName);
        var index = 1;
        while (true)
        {
            var nextName = $"{stem} ({index}){ext}";
            var nextPath = Path.Combine(destinationDirectory, nextName);
            if (!File.Exists(nextPath))
            {
                return nextPath;
            }

            index++;
        }
    }

    private static void MoveWithUniqueName(string sourcePath, string destinationDirectory)
    {
        var destinationPath = EnsureUniqueDestinationPath(destinationDirectory, Path.GetFileName(sourcePath));
        File.Move(sourcePath, destinationPath);
    }

    private static string GetRootDirectoryName(string serverType)
    {
        if (PluginServerTypes.Contains(serverType))
        {
            return "plugins";
        }

        if (ModServerTypes.Contains(serverType))
        {
            return "mods";
        }

        throw new InvalidOperationException($"未対応のサーバー種別です: {serverType}");
    }

    private static void EnsureSupported(string serverType)
    {
        if (!PluginServerTypes.Contains(serverType) && !ModServerTypes.Contains(serverType))
        {
            throw new InvalidOperationException($"このサーバー種別はMOD/プラグイン管理に未対応です: {serverType}");
        }
    }
}
