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
    public void Load_WhenJsonIsBroken_RejectsWithoutResettingSettings()
    {
        using var appData = new TemporaryAppDataScope();
        var paths = new AppPathsService();
        Directory.CreateDirectory(Path.GetDirectoryName(paths.AppSettingsPath)!);
        File.WriteAllText(paths.AppSettingsPath, "{ this is invalid json");
        var service = new AppSettingsService(paths);

        var error = Assert.Throws<AppSettingsStorageException>(() => service.Load());
        Assert.Equal(AppSettingsStorageError.InvalidData, error.Reason);
        Assert.Throws<AppSettingsStorageException>(() => service.Save(new AppSettings()));
        Assert.Equal("{ this is invalid json", File.ReadAllText(paths.AppSettingsPath));
    }

    [Fact]
    public void Update_KeepsValuesSavedByOtherScreens()
    {
        using var appData = new TemporaryAppDataScope();
        IAppSettingsService service = new AppSettingsService(new AppPathsService());

        // 画面 A が起動時に読み込んだ古い設定
        var staleCopy = service.Load();
        // 画面 B が UPnP の選択を保存
        service.Update(s =>
        {
            s.EnableUpnp = true;
            s.PromptUpnp = false;
        });
        // 画面 A がテーマだけを変更
        staleCopy.Theme = ThemeService.LightTheme;
        service.Update(s => s.Theme = staleCopy.Theme);

        var saved = service.Load();
        Assert.Equal(ThemeService.LightTheme, saved.Theme);
        Assert.True(saved.EnableUpnp);
        Assert.False(saved.PromptUpnp);
    }
}
