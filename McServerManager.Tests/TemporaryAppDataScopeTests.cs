using McServerManager.Services;
using McServerManager.Tests.TestInfrastructure;

namespace McServerManager.Tests;

public sealed class TemporaryAppDataScopeTests
{
    [Fact]
    public void TemporaryAppDataScope_IsolatesPaths_AndCleansUp()
    {
        var originalOverride = Environment.GetEnvironmentVariable(AppPathsService.AppDataRootOverrideEnvVar);
        var originalAppData = Environment.GetEnvironmentVariable("APPDATA");
        var originalLocalAppData = Environment.GetEnvironmentVariable("LOCALAPPDATA");
        var originalTemp = Environment.GetEnvironmentVariable("TEMP");
        var originalTmp = Environment.GetEnvironmentVariable("TMP");
        string tempRoot;

        using (var scope = new TemporaryAppDataScope())
        {
            tempRoot = Directory.GetParent(scope.AppDataPath)!.FullName;

            var paths = new AppPathsService();
            Assert.Equal(Path.Combine(scope.AppDataPath, "MaiPilot"), paths.RootPath);
            Assert.StartsWith(tempRoot, paths.RootPath, StringComparison.OrdinalIgnoreCase);

            Assert.Equal(scope.AppDataPath, Environment.GetEnvironmentVariable("APPDATA"));
            Assert.Equal(scope.LocalAppDataPath, Environment.GetEnvironmentVariable("LOCALAPPDATA"));
            Assert.Equal(scope.TempPath, Environment.GetEnvironmentVariable("TEMP"));
            Assert.Equal(scope.TempPath, Environment.GetEnvironmentVariable("TMP"));
        }

        Assert.Equal(originalOverride, Environment.GetEnvironmentVariable(AppPathsService.AppDataRootOverrideEnvVar));
        Assert.Equal(originalAppData, Environment.GetEnvironmentVariable("APPDATA"));
        Assert.Equal(originalLocalAppData, Environment.GetEnvironmentVariable("LOCALAPPDATA"));
        Assert.Equal(originalTemp, Environment.GetEnvironmentVariable("TEMP"));
        Assert.Equal(originalTmp, Environment.GetEnvironmentVariable("TMP"));
        Assert.False(Directory.Exists(tempRoot));
    }
}
