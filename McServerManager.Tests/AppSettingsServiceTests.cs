using McServerManager.Models;
using McServerManager.Services;
using McServerManager.Tests.TestInfrastructure;

namespace McServerManager.Tests;

public sealed class AppSettingsServiceTests
{
    [Fact]
    public void SaveAndLoad_RoundTrips()
    {
        using var appData = new TemporaryAppDataScope();
        var paths = new AppPathsService();
        var service = new AppSettingsService(paths);
        var input = new AppSettings
        {
            HasShownFirstRun = true,
            HasCompletedTutorial = true,
            EnableUpnp = true,
            PromptUpnp = false,
            Theme = ThemeService.LightTheme,
            ServerDirectories = [@"C:\servers\a", @"D:\servers\b"]
        };

        service.Save(input);
        var loaded = service.Load();

        Assert.True(loaded.HasShownFirstRun);
        Assert.True(loaded.HasCompletedTutorial);
        Assert.True(loaded.EnableUpnp);
        Assert.False(loaded.PromptUpnp);
        Assert.Equal(ThemeService.LightTheme, loaded.Theme);
        Assert.Equal(2, loaded.ServerDirectories.Count);
    }

    [Fact]
    public void Load_WhenJsonIsBroken_ReturnsDefaultSettings()
    {
        using var appData = new TemporaryAppDataScope();
        var paths = new AppPathsService();
        Directory.CreateDirectory(Path.GetDirectoryName(paths.AppSettingsPath)!);
        File.WriteAllText(paths.AppSettingsPath, "{ this is invalid json");
        var service = new AppSettingsService(paths);

        var loaded = service.Load();

        Assert.False(loaded.HasShownFirstRun);
        Assert.False(loaded.HasCompletedTutorial);
        Assert.Equal(ThemeService.DarkTheme, loaded.Theme);
        Assert.Empty(loaded.ServerDirectories);
    }
}
