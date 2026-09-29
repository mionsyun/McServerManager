using McServerManager.Services;
using McServerManager.Tests.TestInfrastructure;

namespace McServerManager.Tests;

public sealed class JavaServiceTests
{
    [Theory]
    [InlineData("26.3", 25)]
    [InlineData("26.1-snapshot-2", 25)]
    [InlineData("27.1", 25)]
    [InlineData("1.21.1", 21)]
    [InlineData("1.20.4", 17)]
    [InlineData("1.17.1", 16)]
    [InlineData("1.16.5", 8)]
    public void GetRequiredJavaMajor_HandlesYearBasedAndLegacyVersions(string version, int expected)
    {
        Assert.Equal(expected, new JavaService().GetRequiredJavaMajor(version));
    }

    [Theory]
    [InlineData("25w14a")]
    [InlineData("")]
    [InlineData(null)]
    public void GetRequiredJavaMajor_UnknownFormat_ReturnsNull(string? version)
    {
        Assert.Null(new JavaService().GetRequiredJavaMajor(version));
    }

    [Fact]
    public void FindNewestJava_PicksHighestVersionAcrossVendorFolders()
    {
        using var temp = new TemporaryDirectoryScope();
        var adoptium = Path.Combine(temp.Path, "Eclipse Adoptium");
        var microsoft = Path.Combine(temp.Path, "Microsoft");
        CreateJavaHome(Path.Combine(adoptium, "jdk-8.0.392.8-hotspot"), "1.8.0_392");
        var newest = CreateJavaHome(Path.Combine(adoptium, "jdk-25.0.1.8-hotspot"), "25.0.1");
        CreateJavaHome(Path.Combine(microsoft, "jdk-21.0.5.11-hotspot"), "21.0.5");
        Directory.CreateDirectory(Path.Combine(microsoft, "Edge")); // java.exe の無いフォルダは無視

        var found = JavaService.FindNewestJava(new[] { adoptium, microsoft, Path.Combine(temp.Path, "missing") });

        Assert.Equal(newest, found);
    }

    [Fact]
    public void FindNewestJava_WithoutAnyJava_ReturnsNull()
    {
        using var temp = new TemporaryDirectoryScope();
        Assert.Null(JavaService.FindNewestJava(new[] { temp.Path }));
    }

    private static string CreateJavaHome(string home, string javaVersion)
    {
        var bin = Directory.CreateDirectory(Path.Combine(home, "bin")).FullName;
        var javaExe = Path.Combine(bin, "java.exe");
        File.WriteAllText(javaExe, string.Empty);
        File.WriteAllText(Path.Combine(home, "release"), $"IMPLEMENTOR=\"Test\"\nJAVA_VERSION=\"{javaVersion}\"\n");
        return javaExe;
    }
}
