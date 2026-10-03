using McServerManager.Models.Templates;
using static McServerManager.Services.Templates.TemplateJsonFields;

namespace McServerManager.Services.Templates;

/// <summary>Local consistency checks. This does not replace provider or archive metadata verification.</summary>
internal static class TemplateManifestConsistency
{
    public static void ValidateFileName(string fileName, string path)
    {
        // Do not use Path.GetInvalidFileNameChars: it differs on Linux and Windows.
        if (!fileName.EndsWith(".jar", StringComparison.Ordinal) ||
            fileName.Length < 5 || fileName.Length > 184 ||
            char.IsWhiteSpace(fileName[0]) || fileName.EndsWith(' ') || fileName.EndsWith('.') ||
            fileName.Any(character => char.IsControl(character) || "<>:\"/\\|?*".Contains(character)))
            Fail("UnsafeFileName", path, "Expected a safe Windows .jar file name without a path.");

        var deviceName = fileName.Split('.')[0].TrimEnd(' ', '.');
        var reserved = deviceName.Equals("CON", StringComparison.OrdinalIgnoreCase) ||
            deviceName.Equals("PRN", StringComparison.OrdinalIgnoreCase) ||
            deviceName.Equals("AUX", StringComparison.OrdinalIgnoreCase) ||
            deviceName.Equals("NUL", StringComparison.OrdinalIgnoreCase) ||
            deviceName.Equals("CONIN$", StringComparison.OrdinalIgnoreCase) ||
            deviceName.Equals("CONOUT$", StringComparison.OrdinalIgnoreCase);
        if (deviceName.Length == 4 &&
            (deviceName.StartsWith("COM", StringComparison.OrdinalIgnoreCase) || deviceName.StartsWith("LPT", StringComparison.OrdinalIgnoreCase)) &&
            "123456789¹²³".Contains(deviceName[3]))
            reserved = true;
        if (reserved)
            Fail("UnsafeFileName", path, "Windows reserved device names are not allowed.");
    }

    public static void Validate(TemplateManifest manifest)
    {
        var entries = new Dictionary<string, TemplateAddon>(StringComparer.Ordinal);
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var projects = new HashSet<string>(StringComparer.Ordinal);
        var hashes = new HashSet<string>(StringComparer.Ordinal);
        foreach (var addon in manifest.Addons)
        {
            if (!entries.TryAdd(addon.EntryId, addon))
                Fail("DuplicateEntryId", "$.addons", "Addon entry identifiers must be unique.");
            if (!names.Add(addon.Source.FileName))
                Fail("DuplicateFileName", "$.addons", "Addon file names must not collide on Windows.");
            if (!projects.Add(addon.Source.Provider + ":" + addon.Source.ProjectId))
                Fail("DuplicateProject", "$.addons", "Multiple entries from the same provider project are not allowed.");
            if (!hashes.Add(addon.Sha256))
                Fail("DuplicateArtifact", "$.addons", "The same addon artifact must not be included twice.");
            if (manifest.Runtime.Type == "vanilla" ||
                (manifest.Runtime.Type == "paper" && addon.Kind != "plugin") ||
                (manifest.Runtime.Type is "fabric" or "forge" && addon.Kind != "mod"))
                Fail("IncompatibleAddonKind", "$.addons", "The addon kind does not match this runtime family.");
        }

        foreach (var addon in manifest.Addons)
            foreach (var reference in addon.Requires)
            {
                if (!entries.ContainsKey(reference))
                    Fail("MissingDependency", "$.addons", "A dependency references an entry absent from this manifest.");
                if (reference == addon.EntryId)
                    Fail("DependencyCycle", "$.addons", "An addon cannot require itself.");
            }

        var active = new HashSet<string>(StringComparer.Ordinal);
        var depths = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var id in entries.Keys)
            Visit(id, entries, active, depths);
    }

    private static int Visit(string id, IReadOnlyDictionary<string, TemplateAddon> entries,
        HashSet<string> active, Dictionary<string, int> depths)
    {
        if (depths.TryGetValue(id, out var knownDepth))
            return knownDepth;
        if (!active.Add(id))
            Fail("DependencyCycle", "$.addons", "Dependency cycles are unsupported.");
        if (active.Count > TemplatePolicy.MaxDependencyDepth)
            Fail("DependencyDepthExceeded", "$.addons", "The dependency graph exceeds the maximum depth of 16 entries.");
        var depth = 1;
        foreach (var reference in entries[id].Requires)
            depth = Math.Max(depth, 1 + Visit(reference, entries, active, depths));
        if (depth > TemplatePolicy.MaxDependencyDepth)
            Fail("DependencyDepthExceeded", "$.addons", "The dependency graph exceeds the maximum depth of 16 entries.");
        active.Remove(id);
        depths.Add(id, depth);
        return depth;
    }
}
