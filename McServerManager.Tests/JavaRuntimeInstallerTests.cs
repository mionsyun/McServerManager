using System.IO.Compression;
using McServerManager.Services;
using McServerManager.Tests.TestInfrastructure;

namespace McServerManager.Tests;

public sealed class JavaRuntimeInstallerTests
{
    private const string AdoptiumJson = """
        [{
          "binary": {
            "image_type": "jre", "os": "windows", "architecture": "x64",
            "installer": { "link": "https://github.com/adoptium/temurin21-binaries/releases/download/jdk-21.0.12.1%2B1/OpenJDK21U-jre_x64_windows_hotspot_21.0.12.1_1.msi" },
            "package": {
              "link": "https://github.com/adoptium/temurin21-binaries/releases/download/jdk-21.0.12.1%2B1/OpenJDK21U-jre_x64_windows_hotspot_21.0.12.1_1.zip",
              "checksum": "d35f31e712f0aa11", "size": 49283072
            }
          },
          "release_name": "jdk-21.0.12.1+1"
        }]
        """;

    [Theory]
    [InlineData(null, 21)]
    [InlineData(8, 8)]
    [InlineData(16, 17)]
    [InlineData(17, 17)]
    [InlineData(21, 21)]
    [InlineData(25, 25)]
    [InlineData(27, 27)]
    public void GetInstallMajor_PicksNearestLtsAtOrAboveRequirement(int? required, int expected)
    {
        using var appData = new TemporaryAppDataScope();
        var installer = new JavaRuntimeInstaller(new AppPathsService(), new JavaService());

        Assert.Equal(expected, installer.GetInstallMajor(required));
    }

    [Fact]
    public void ParseLatestAsset_ReadsZipPackageNotInstaller()
    {
        var asset = JavaRuntimeInstaller.ParseLatestAsset(AdoptiumJson);

        Assert.EndsWith(".zip", asset.DownloadUrl);
        Assert.Equal("d35f31e712f0aa11", asset.Sha256);
        Assert.Equal(49283072, asset.Size);
        Assert.Equal("jdk-21.0.12.1+1", asset.ReleaseName);
    }

    [Theory]
    [InlineData("https://github.com/adoptium/temurin21-binaries/releases/download/x/a.zip", true)]
    [InlineData("http://github.com/adoptium/temurin21-binaries/releases/download/x/a.zip", false)]
    [InlineData("https://github.com/someone-else/java/releases/download/x/a.zip", false)]
    [InlineData("https://example.com/adoptium/a.zip", false)]
    public void IsTrustedDownloadUrl_AllowsOnlyAdoptiumOnGitHub(string url, bool expected)
    {
        Assert.Equal(expected, JavaRuntimeInstaller.IsTrustedDownloadUrl(url));
    }

    [Fact]
    public void ExtractJavaHome_FindsJavaHomeOneLevelDown()
    {
        using var temp = new TemporaryDirectoryScope();
        var zip = Path.Combine(temp.Path, "jre.zip");
        using (var archive = ZipFile.Open(zip, ZipArchiveMode.Create))
        {
            archive.CreateEntry("jdk-21.0.12.1+1-jre/bin/java.exe");
            archive.CreateEntry("jdk-21.0.12.1+1-jre/release");
        }

        var extractDir = Path.Combine(temp.Path, "out");
        JavaRuntimeInstaller.ExtractJavaHome(zip, extractDir);

        Assert.Equal(Path.Combine(extractDir, "jdk-21.0.12.1+1-jre"), JavaRuntimeInstaller.FindJavaHome(extractDir));
    }

    [Fact]
    public void ExtractJavaHome_RejectsPathTraversal()
    {
        using var temp = new TemporaryDirectoryScope();
        var zip = Path.Combine(temp.Path, "evil.zip");
        using (var archive = ZipFile.Open(zip, ZipArchiveMode.Create))
        {
            archive.CreateEntry("../evil.txt");
        }

        Assert.Throws<InvalidOperationException>(() => JavaRuntimeInstaller.ExtractJavaHome(zip, Path.Combine(temp.Path, "out")));
        Assert.False(File.Exists(Path.Combine(temp.Path, "evil.txt")));
    }

    [SkippableFact]
    public async Task InstallAsync_LiveDownload_ProducesWorkingJava()
    {
        Skip.IfNot(
            LiveTestGate.IsLiveEnabled() && LiveTestGate.IsLiveDownloadEnabled(),
            $"Set {LiveTestGate.LiveEnvVar}=1 and {LiveTestGate.LiveDownloadEnvVar}=1 to run live download tests.");

        using var appData = new TemporaryAppDataScope();
        var javaService = new JavaService();
        var installer = new JavaRuntimeInstaller(new AppPathsService(), javaService);
        var progress = new List<string>();

        var javaExe = await installer.InstallAsync(21, new Progress<string>(progress.Add));

        Assert.True(File.Exists(javaExe));
        Assert.True(javaService.TryGetJavaMajorVersion(javaExe, out var major, out _));
        Assert.Equal(21, major);
        Assert.Equal(javaExe, installer.FindInstalled(21));
        // 2 回目は導入済みのものを返し、ダウンロードしない
        Assert.Equal(javaExe, await installer.InstallAsync(21));
    }
}
