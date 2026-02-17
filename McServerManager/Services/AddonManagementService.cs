using McServerManager.Models;
using System.IO.Compression;
using System.Text.Json;
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
    private static readonly Regex ForgeModIdRegex = new(@"modId\s*=\s*\""""(?<id>[A-Za-z0-9_\-\.]+)\""""", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex ForgeDependencyBlockRegex = new(@"\[\[dependencies\.[^\]]+\]\](?<body>[\s\S]*?)(?=(\r?\n\[\[)|$)", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex ForgeMandatoryRegex = new(@"mandatory\s*=\s*(?<value>true|false)", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex ForgeDependencyIdRegex = new(@"modId\s*=\s*\""""(?<id>[A-Za-z0-9_\-\.]+)\""""", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private enum AddonKind
    {
        Unknown,
        Plugin,
        FabricMod,
        ForgeMod
    }

    private sealed record AddonMetadata(
        AddonKind Kind,
        string DeclaredId,
        IReadOnlyList<string> RequiredDependencies,
        IReadOnlyList<string> ParseWarnings);

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

    public AddonImportAssessment AssessImport(string serverType, IEnumerable<string> sourcePaths, IEnumerable<AddonEntry> existingAddons)
    {
        EnsureSupported(serverType);

        var candidates = ExpandJarFiles(sourcePaths)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (candidates.Count == 0)
        {
            return new AddonImportAssessment
            {
                CandidateCount = 0,
                Warnings = Array.Empty<string>()
            };
        }

        var warnings = new List<string>();
        var existingIds = new HashSet<string>(
            existingAddons.Select(entry => NormalizeAddonId(NormalizeAddonKey(entry.FileName))),
            StringComparer.OrdinalIgnoreCase);

        var importedIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var importedDependencies = new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase);

        foreach (var filePath in candidates)
        {
            var fileName = Path.GetFileName(filePath);
            var metadata = InspectJar(filePath);
            var fallbackId = NormalizeAddonId(NormalizeAddonKey(fileName));
            var addonId = NormalizeAddonId(string.IsNullOrWhiteSpace(metadata.DeclaredId) ? fallbackId : metadata.DeclaredId);

            importedIds.Add(addonId);
            importedDependencies[addonId] = metadata.RequiredDependencies;

            foreach (var parseWarning in metadata.ParseWarnings)
            {
                warnings.Add($"{fileName}: {parseWarning}");
            }

            var kindWarning = BuildKindWarning(serverType, metadata.Kind, fileName);
            if (!string.IsNullOrWhiteSpace(kindWarning))
            {
                warnings.Add(kindWarning);
            }

            if (metadata.Kind == AddonKind.Unknown)
            {
                warnings.Add($"{fileName}: 種別を判定できませんでした（互換性を確認してください）。");
            }
        }

        var allKnownIds = new HashSet<string>(existingIds, StringComparer.OrdinalIgnoreCase);
        allKnownIds.UnionWith(importedIds);

        foreach (var pair in importedDependencies)
        {
            var addonId = pair.Key;
            foreach (var requiredDependency in pair.Value)
            {
                if (allKnownIds.Contains(requiredDependency))
                {
                    continue;
                }

                warnings.Add($"{addonId}: 必須依存の可能性 {requiredDependency} が見つかりません。\n（同時追加または既存導入を確認してください）");
            }
        }

        return new AddonImportAssessment
        {
            CandidateCount = candidates.Count,
            Warnings = warnings
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList()
        };
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

    private static string NormalizeAddonId(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        return value.Trim().Replace(' ', '-').ToLowerInvariant();
    }

    private static AddonMetadata InspectJar(string jarPath)
    {
        try
        {
            using var archive = ZipFile.OpenRead(jarPath);

            var fabricEntry = archive.GetEntry("fabric.mod.json");
            var forgeEntry = archive.GetEntry("META-INF/mods.toml") ?? archive.GetEntry("mods.toml");
            var pluginEntry = archive.GetEntry("plugin.yml") ?? archive.GetEntry("paper-plugin.yml");

            var parseWarnings = new List<string>();

            if (fabricEntry is not null)
            {
                return ParseFabricMetadata(fabricEntry, parseWarnings);
            }

            if (forgeEntry is not null)
            {
                return ParseForgeMetadata(forgeEntry, parseWarnings);
            }

            if (pluginEntry is not null)
            {
                return ParsePluginMetadata(pluginEntry, parseWarnings);
            }

            return new AddonMetadata(AddonKind.Unknown, string.Empty, Array.Empty<string>(), parseWarnings);
        }
        catch (Exception ex)
        {
            return new AddonMetadata(
                AddonKind.Unknown,
                string.Empty,
                Array.Empty<string>(),
                new[] { $"jar解析に失敗しました: {ex.Message}" });
        }
    }

    private static AddonMetadata ParseFabricMetadata(ZipArchiveEntry entry, List<string> parseWarnings)
    {
        try
        {
            using var stream = entry.Open();
            using var doc = JsonDocument.Parse(stream);
            var root = doc.RootElement;

            var declaredId = root.TryGetProperty("id", out var idElement)
                ? idElement.GetString() ?? string.Empty
                : string.Empty;

            var required = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (root.TryGetProperty("depends", out var dependsElement) && dependsElement.ValueKind == JsonValueKind.Object)
            {
                foreach (var property in dependsElement.EnumerateObject())
                {
                    var dependencyId = NormalizeAddonId(property.Name);
                    if (IsIgnorableDependency(dependencyId))
                    {
                        continue;
                    }

                    required.Add(dependencyId);
                }
            }

            return new AddonMetadata(
                AddonKind.FabricMod,
                declaredId,
                required.ToList(),
                parseWarnings);
        }
        catch (Exception ex)
        {
            parseWarnings.Add($"fabric.mod.json の解析に失敗しました: {ex.Message}");
            return new AddonMetadata(AddonKind.FabricMod, string.Empty, Array.Empty<string>(), parseWarnings);
        }
    }

    private static AddonMetadata ParseForgeMetadata(ZipArchiveEntry entry, List<string> parseWarnings)
    {
        try
        {
            using var stream = entry.Open();
            using var reader = new StreamReader(stream);
            var text = reader.ReadToEnd();

            var declaredId = ForgeModIdRegex.Matches(text)
                .Select(match => match.Groups["id"].Value)
                .FirstOrDefault(value => !string.IsNullOrWhiteSpace(value))
                ?? string.Empty;

            var required = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (Match block in ForgeDependencyBlockRegex.Matches(text))
            {
                var body = block.Groups["body"].Value;
                var mandatoryMatch = ForgeMandatoryRegex.Match(body);
                var isMandatory = !mandatoryMatch.Success || string.Equals(mandatoryMatch.Groups["value"].Value, "true", StringComparison.OrdinalIgnoreCase);
                if (!isMandatory)
                {
                    continue;
                }

                var idMatch = ForgeDependencyIdRegex.Match(body);
                if (!idMatch.Success)
                {
                    continue;
                }

                var dependencyId = NormalizeAddonId(idMatch.Groups["id"].Value);
                if (IsIgnorableDependency(dependencyId))
                {
                    continue;
                }

                required.Add(dependencyId);
            }

            return new AddonMetadata(
                AddonKind.ForgeMod,
                declaredId,
                required.ToList(),
                parseWarnings);
        }
        catch (Exception ex)
        {
            parseWarnings.Add($"mods.toml の解析に失敗しました: {ex.Message}");
            return new AddonMetadata(AddonKind.ForgeMod, string.Empty, Array.Empty<string>(), parseWarnings);
        }
    }

    private static AddonMetadata ParsePluginMetadata(ZipArchiveEntry entry, List<string> parseWarnings)
    {
        try
        {
            using var stream = entry.Open();
            using var reader = new StreamReader(stream);
            var yaml = reader.ReadToEnd();

            var declaredId = ReadYamlScalar(yaml, "name");
            var required = ReadYamlList(yaml, "depend")
                .Select(NormalizeAddonId)
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .ToList();

            return new AddonMetadata(
                AddonKind.Plugin,
                declaredId,
                required,
                parseWarnings);
        }
        catch (Exception ex)
        {
            parseWarnings.Add($"plugin.yml の解析に失敗しました: {ex.Message}");
            return new AddonMetadata(AddonKind.Plugin, string.Empty, Array.Empty<string>(), parseWarnings);
        }
    }

    private static string? BuildKindWarning(string serverType, AddonKind addonKind, string fileName)
    {
        if (string.Equals(serverType, "Fabric", StringComparison.OrdinalIgnoreCase) && addonKind == AddonKind.ForgeMod)
        {
            return $"{fileName}: FabricサーバーでForge系MODの可能性があります。";
        }

        if (string.Equals(serverType, "Forge", StringComparison.OrdinalIgnoreCase) && addonKind == AddonKind.FabricMod)
        {
            return $"{fileName}: ForgeサーバーでFabric系MODの可能性があります。";
        }

        if (PluginServerTypes.Contains(serverType) && (addonKind == AddonKind.FabricMod || addonKind == AddonKind.ForgeMod))
        {
            return $"{fileName}: プラグインサーバーでMOD系ファイルの可能性があります。";
        }

        if (ModServerTypes.Contains(serverType) && addonKind == AddonKind.Plugin)
        {
            return $"{fileName}: MODサーバーでプラグイン系ファイルの可能性があります。";
        }

        return null;
    }

    private static bool IsIgnorableDependency(string dependencyId)
    {
        return dependencyId is "minecraft" or "java" or "forge" or "fabricloader";
    }

    private static string ReadYamlScalar(string yaml, string key)
    {
        foreach (var line in yaml.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None))
        {
            var trimmed = line.Trim();
            if (trimmed.StartsWith("#", StringComparison.Ordinal) || !trimmed.StartsWith($"{key}:", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var value = trimmed[(key.Length + 1)..].Trim();
            return value.Trim('"', '\'');
        }

        return string.Empty;
    }

    private static IReadOnlyList<string> ReadYamlList(string yaml, string key)
    {
        var result = new List<string>();
        var lines = yaml.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None);

        for (var i = 0; i < lines.Length; i++)
        {
            var line = lines[i].Trim();
            if (!line.StartsWith($"{key}:", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var inline = line[(key.Length + 1)..].Trim();
            if (inline.StartsWith("[", StringComparison.Ordinal) && inline.EndsWith("]", StringComparison.Ordinal))
            {
                var body = inline[1..^1];
                foreach (var value in body.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                {
                    result.Add(value.Trim('"', '\''));
                }

                break;
            }

            for (var j = i + 1; j < lines.Length; j++)
            {
                var item = lines[j].Trim();
                if (item.StartsWith("-", StringComparison.Ordinal))
                {
                    result.Add(item[1..].Trim().Trim('"', '\''));
                    continue;
                }

                if (!string.IsNullOrWhiteSpace(item))
                {
                    break;
                }
            }

            break;
        }

        return result;
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
