using System.IO.Compression;
using System.Text.Json;

namespace McServerManager.Tests.TestInfrastructure;

internal static class JarTestFactory
{
    public static string CreatePluginJar(
        string directory,
        string fileName,
        string pluginName,
        IReadOnlyList<string>? dependencies = null)
    {
        Directory.CreateDirectory(directory);
        var jarPath = Path.Combine(directory, fileName);
        using var archive = ZipFile.Open(jarPath, ZipArchiveMode.Create);
        var entry = archive.CreateEntry("plugin.yml");

        using var writer = new StreamWriter(entry.Open());
        writer.WriteLine($"name: {pluginName}");
        writer.WriteLine("version: 1.0.0");
        writer.WriteLine("main: com.example.Main");
        if (dependencies is { Count: > 0 })
        {
            writer.WriteLine("depend:");
            foreach (var dependency in dependencies)
            {
                writer.WriteLine($"  - {dependency}");
            }
        }

        return jarPath;
    }

    public static string CreateFabricModJar(
        string directory,
        string fileName,
        string modId,
        IReadOnlyDictionary<string, string>? depends = null)
    {
        Directory.CreateDirectory(directory);
        var jarPath = Path.Combine(directory, fileName);
        using var archive = ZipFile.Open(jarPath, ZipArchiveMode.Create);
        var entry = archive.CreateEntry("fabric.mod.json");
        using var stream = entry.Open();

        var payload = new Dictionary<string, object?>
        {
            ["schemaVersion"] = 1,
            ["id"] = modId,
            ["version"] = "1.0.0"
        };
        if (depends is not null && depends.Count > 0)
        {
            payload["depends"] = depends;
        }

        JsonSerializer.Serialize(stream, payload);
        return jarPath;
    }

    public static string CreateForgeModJar(
        string directory,
        string fileName,
        string modId,
        IReadOnlyList<string>? requiredDependencies = null)
    {
        Directory.CreateDirectory(directory);
        var jarPath = Path.Combine(directory, fileName);
        using var archive = ZipFile.Open(jarPath, ZipArchiveMode.Create);
        var entry = archive.CreateEntry("META-INF/mods.toml");

        using var writer = new StreamWriter(entry.Open());
        writer.WriteLine("modLoader=\"javafml\"");
        writer.WriteLine("loaderVersion=\"[47,)\"");
        writer.WriteLine("license=\"MIT\"");
        writer.WriteLine();
        writer.WriteLine("[[mods]]");
        writer.WriteLine($"modId=\"{modId}\"");
        writer.WriteLine("version=\"1.0.0\"");
        writer.WriteLine("displayName=\"Example Forge Mod\"");

        if (requiredDependencies is { Count: > 0 })
        {
            foreach (var dependency in requiredDependencies)
            {
                writer.WriteLine();
                writer.WriteLine($"[[dependencies.{modId}]]");
                writer.WriteLine($"modId=\"{dependency}\"");
                writer.WriteLine("mandatory=true");
                writer.WriteLine("versionRange=\"[1.0,)\"");
                writer.WriteLine("ordering=\"NONE\"");
                writer.WriteLine("side=\"BOTH\"");
            }
        }

        return jarPath;
    }
}
