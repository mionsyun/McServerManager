using McServerManager.Models;
using McServerManager.Services;
using McServerManager.Tests.TestInfrastructure;

namespace McServerManager.Tests;

public sealed class AddonManagementE2ETests
{
    [Fact]
    public void PluginLifecycle_Paper_EndToEnd()
    {
        using var temp = new TemporaryDirectoryScope();
        var service = new AddonManagementService();
        var serverDirectory = Directory.CreateDirectory(Path.Combine(temp.Path, "paper-server")).FullName;
        var sourceDirectory = Directory.CreateDirectory(Path.Combine(temp.Path, "source")).FullName;

        var pluginJar = JarTestFactory.CreatePluginJar(sourceDirectory, "example-plugin.jar", "ExamplePlugin");

        var assessment = service.AssessImport("Paper", new[] { pluginJar }, Array.Empty<AddonEntry>());
        Assert.Equal(1, assessment.CandidateCount);
        Assert.Empty(assessment.Warnings);

        var copied = service.AddFiles(serverDirectory, "Paper", new[] { pluginJar });
        Assert.Equal(1, copied);

        var current = service.GetAddons(serverDirectory, "Paper");
        var enabled = Assert.Single(current);
        Assert.True(enabled.IsEnabled);
        Assert.True(File.Exists(enabled.FullPath));
        Assert.Contains($"{Path.DirectorySeparatorChar}plugins{Path.DirectorySeparatorChar}", enabled.FullPath, StringComparison.OrdinalIgnoreCase);

        service.Disable(serverDirectory, "Paper", enabled);
        current = service.GetAddons(serverDirectory, "Paper");
        var disabled = Assert.Single(current);
        Assert.False(disabled.IsEnabled);
        Assert.Contains($"{Path.DirectorySeparatorChar}disabled{Path.DirectorySeparatorChar}", disabled.FullPath, StringComparison.OrdinalIgnoreCase);

        service.Enable(serverDirectory, "Paper", disabled);
        current = service.GetAddons(serverDirectory, "Paper");
        enabled = Assert.Single(current);
        Assert.True(enabled.IsEnabled);

        service.Delete(enabled);
        Assert.Empty(service.GetAddons(serverDirectory, "Paper"));
    }

    [Fact]
    public void AssessImport_FabricServer_WarnsForForgeModAndMissingDependency()
    {
        using var temp = new TemporaryDirectoryScope();
        var service = new AddonManagementService();
        var sourceDirectory = Directory.CreateDirectory(Path.Combine(temp.Path, "source")).FullName;
        var forgeJar = JarTestFactory.CreateForgeModJar(
            sourceDirectory,
            "example-forge.jar",
            "exampleforge",
            requiredDependencies: new[] { "missinglib" });

        var assessment = service.AssessImport("Fabric", new[] { forgeJar }, Array.Empty<AddonEntry>());

        Assert.Equal(1, assessment.CandidateCount);
        Assert.NotEmpty(assessment.Warnings);
        Assert.Contains(assessment.Warnings, warning => warning.Contains("Forge", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(assessment.Warnings, warning => warning.Contains("missinglib", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void AssessImport_ForgeServer_WarnsForFabricModAndMissingDependency()
    {
        using var temp = new TemporaryDirectoryScope();
        var service = new AddonManagementService();
        var sourceDirectory = Directory.CreateDirectory(Path.Combine(temp.Path, "source")).FullName;
        var fabricJar = JarTestFactory.CreateFabricModJar(
            sourceDirectory,
            "example-fabric.jar",
            "examplefabric",
            depends: new Dictionary<string, string>
            {
                ["fabricloader"] = ">=0.15.0",
                ["requiredmod"] = "*"
            });

        var assessment = service.AssessImport("Forge", new[] { fabricJar }, Array.Empty<AddonEntry>());

        Assert.Equal(1, assessment.CandidateCount);
        Assert.NotEmpty(assessment.Warnings);
        Assert.Contains(assessment.Warnings, warning => warning.Contains("Fabric", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(assessment.Warnings, warning => warning.Contains("requiredmod", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void AssessImport_PluginDependency_UsesExistingPluginAsSatisfied()
    {
        using var temp = new TemporaryDirectoryScope();
        var service = new AddonManagementService();
        var serverDirectory = Directory.CreateDirectory(Path.Combine(temp.Path, "paper-server")).FullName;
        var sourceDirectory = Directory.CreateDirectory(Path.Combine(temp.Path, "source")).FullName;

        var existingPluginJar = JarTestFactory.CreatePluginJar(sourceDirectory, "worldedit-7.0.jar", "WorldEdit");
        service.AddFiles(serverDirectory, "Paper", new[] { existingPluginJar });
        var existingAddons = service.GetAddons(serverDirectory, "Paper");

        var targetPluginJar = JarTestFactory.CreatePluginJar(
            sourceDirectory,
            "essentialsx.jar",
            "EssentialsX",
            dependencies: new[] { "WorldEdit", "Vault" });

        var assessment = service.AssessImport("Paper", new[] { targetPluginJar }, existingAddons);

        Assert.Equal(1, assessment.CandidateCount);
        Assert.Contains(assessment.Warnings, warning => warning.Contains("vault", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(assessment.Warnings, warning => warning.Contains("worldedit", StringComparison.OrdinalIgnoreCase));
    }
}
