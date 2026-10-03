using System.Text.Json;
using McServerManager.Models.Templates;
using static McServerManager.Services.Templates.TemplateJsonFields;

namespace McServerManager.Services.Templates;

internal static class TemplateManifestReader
{
    private static readonly string[] RootKeys = ["schemaVersion", "templateId", "revision", "name", "description", "edition", "runtime", "settings", "addons"];
    private static readonly string[] RuntimeKeys = ["type", "minecraftVersion", "build", "loaderVersion", "installerVersion"];
    private static readonly string[] SettingsKeys = ["difficulty", "gamemode", "maxPlayers", "viewDistance", "simulationDistance", "pvp", "spawnProtection"];
    private static readonly string[] AddonKeys = ["entryId", "kind", "selection", "source", "sha256", "sizeBytes", "requires"];
    private static readonly string[] ModrinthKeys = ["provider", "projectId", "versionId", "fileName", "sha512"];
    private static readonly string[] CurseForgeKeys = ["provider", "projectId", "fileId", "fileName", "sha1"];
    private const string EntryIdPattern = "\\A[a-z][a-z0-9-]{0,63}\\z";
    private const string VersionPattern = "\\A[0-9]+\\.[0-9]+\\.[0-9]+\\z";

    public static TemplateManifest Read(JsonElement root)
    {
        Object(root, "$", RootKeys, RootKeys);
        var schema = Choice(root.GetProperty("schemaVersion"), "$.schemaVersion", "1.0");
        var id = Text(root.GetProperty("templateId"), "$.templateId", 36, 36);
        if (!Guid.TryParseExact(id, "D", out var templateId))
            Fail("InvalidValue", "$.templateId", "Expected a UUID in standard hyphenated form.");
        return new TemplateManifest
        {
            SchemaVersion = schema,
            TemplateId = templateId,
            Revision = (int)Integer(root.GetProperty("revision"), "$.revision", 1, int.MaxValue),
            Name = Text(root.GetProperty("name"), "$.name", 1, 80),
            Description = Text(root.GetProperty("description"), "$.description", 0, 1000),
            Edition = Choice(root.GetProperty("edition"), "$.edition", "java"),
            Runtime = ReadRuntime(root.GetProperty("runtime")),
            Settings = ReadSettings(root.GetProperty("settings")),
            Addons = ReadAddons(root.GetProperty("addons"))
        };
    }

    private static TemplateRuntime ReadRuntime(JsonElement value)
    {
        const string path = "$.runtime";
        Object(value, path, RuntimeKeys, ["type", "minecraftVersion"]);
        var type = Choice(value.GetProperty("type"), path + ".type", "vanilla", "paper", "fabric", "forge");
        var minecraft = Text(value.GetProperty("minecraftVersion"), path + ".minecraftVersion", 1,
            TemplatePolicy.MaxRuntimeVersionLength, "\\A[0-9]+\\.[0-9]+(?:\\.[0-9]+)?\\z");
        string? build = null, loader = null, installer = null;
        switch (type)
        {
            case "vanilla":
                RejectPins(value, "build", "loaderVersion", "installerVersion");
                break;
            case "paper":
                RejectPins(value, "loaderVersion", "installerVersion");
                build = Pin(value, "build", "\\A[1-9][0-9]*\\z");
                break;
            case "fabric":
                RejectPins(value, "build");
                loader = Pin(value, "loaderVersion", VersionPattern);
                installer = Pin(value, "installerVersion", VersionPattern);
                break;
            case "forge":
                RejectPins(value, "loaderVersion", "installerVersion");
                build = Pin(value, "build", VersionPattern);
                break;
        }
        return new TemplateRuntime { Type = type, MinecraftVersion = minecraft, Build = build, LoaderVersion = loader, InstallerVersion = installer };
    }

    private static void RejectPins(JsonElement runtime, params string[] names)
    {
        foreach (var name in names)
            if (runtime.TryGetProperty(name, out _))
                Fail("InvalidRuntimePin", "$.runtime." + name, "This runtime does not allow this pin.");
    }

    private static string Pin(JsonElement runtime, string name, string pattern)
    {
        if (!runtime.TryGetProperty(name, out var pin))
            Fail("MissingRuntimePin", "$.runtime." + name, "An exact runtime pin is required.");
        return Text(pin, "$.runtime." + name, 1, TemplatePolicy.MaxRuntimeVersionLength, pattern);
    }

    private static TemplateSettings ReadSettings(JsonElement value)
    {
        const string path = "$.settings";
        Object(value, path, SettingsKeys, []);
        return new TemplateSettings
        {
            Difficulty = value.TryGetProperty("difficulty", out var difficulty) ? Choice(difficulty, path + ".difficulty", "peaceful", "easy", "normal", "hard") : null,
            Gamemode = value.TryGetProperty("gamemode", out var gamemode) ? Choice(gamemode, path + ".gamemode", "survival", "creative", "adventure", "spectator") : null,
            MaxPlayers = OptionalInteger(value, "maxPlayers", 1, 1000),
            ViewDistance = OptionalInteger(value, "viewDistance", 2, 32),
            SimulationDistance = OptionalInteger(value, "simulationDistance", 2, 32),
            Pvp = value.TryGetProperty("pvp", out var pvp) ? Boolean(pvp, path + ".pvp") : null,
            SpawnProtection = OptionalInteger(value, "spawnProtection", 0, 256)
        };
    }

    private static int? OptionalInteger(JsonElement settings, string name, int minimum, int maximum) =>
        settings.TryGetProperty(name, out var value) ? (int)Integer(value, "$.settings." + name, minimum, maximum) : null;

    private static IReadOnlyList<TemplateAddon> ReadAddons(JsonElement value)
    {
        Array(value, "$.addons", TemplatePolicy.MaxAddons);
        var addons = new List<TemplateAddon>(value.GetArrayLength());
        long total = 0;
        foreach (var item in value.EnumerateArray())
        {
            var path = $"$.addons[{addons.Count}]";
            Object(item, path, AddonKeys, AddonKeys);
            var size = Integer(item.GetProperty("sizeBytes"), path + ".sizeBytes", 1, TemplatePolicy.MaxAddonBytes);
            total += size; // Maximum 256 * 512 MiB cannot overflow Int64.
            if (total > TemplatePolicy.MaxTotalAddonBytes)
                Fail("TotalSizeExceeded", "$.addons", "The declared addon size exceeds the 2 GiB total limit.");
            var requires = item.GetProperty("requires");
            Array(requires, path + ".requires", TemplatePolicy.MaxAddons);
            var dependencyIds = new List<string>();
            foreach (var dependency in requires.EnumerateArray())
            {
                var id = Text(dependency, path + ".requires", 1, 64, EntryIdPattern);
                if (dependencyIds.Contains(id, StringComparer.Ordinal))
                    Fail("DuplicateDependency", path + ".requires", "Dependency references must be unique.");
                dependencyIds.Add(id);
            }
            addons.Add(new TemplateAddon
            {
                EntryId = Text(item.GetProperty("entryId"), path + ".entryId", 1, 64, EntryIdPattern),
                Kind = Choice(item.GetProperty("kind"), path + ".kind", "mod", "plugin"),
                Selection = Choice(item.GetProperty("selection"), path + ".selection", "direct", "required-dependency", "optional-dependency"),
                Source = ReadSource(item.GetProperty("source"), path + ".source"),
                Sha256 = Text(item.GetProperty("sha256"), path + ".sha256", 64, 64, "\\A[a-f0-9]{64}\\z"),
                SizeBytes = size,
                Requires = dependencyIds.AsReadOnly()
            });
        }
        return addons.AsReadOnly();
    }

    private static TemplateAddonSource ReadSource(JsonElement value, string path)
    {
        var providerValue = default(JsonElement);
        if (value.ValueKind != JsonValueKind.Object || !value.TryGetProperty("provider", out providerValue))
            Fail("MissingProperty", path + ".provider", "A provider identifier is required.");
        var provider = Choice(providerValue, path + ".provider", "modrinth", "curseforge");
        var keys = provider == "modrinth" ? ModrinthKeys : CurseForgeKeys;
        Object(value, path, keys, keys);
        if (provider == "curseforge")
            Fail("ProviderDisabled", path + ".provider", "CurseForge is disabled until its API and persistence permissions are confirmed.");
        var fileName = Text(value.GetProperty("fileName"), path + ".fileName", 5, 184);
        TemplateManifestConsistency.ValidateFileName(fileName, path + ".fileName");
        return new TemplateAddonSource
        {
            Provider = provider,
            ProjectId = Text(value.GetProperty("projectId"), path + ".projectId", 1, 64, "\\A[A-Za-z0-9]{1,64}\\z"),
            VersionId = Text(value.GetProperty("versionId"), path + ".versionId", 1, 64, "\\A[A-Za-z0-9]{1,64}\\z"),
            FileName = fileName,
            Sha512 = Text(value.GetProperty("sha512"), path + ".sha512", 128, 128, "\\A[a-f0-9]{128}\\z")
        };
    }
}
