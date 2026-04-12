using System.Windows;
using McServerManager.Services;

namespace McServerManager.Tests;

public sealed class ThemeServiceTests
{
    [Fact]
    public void Normalize_AndIsDark_BehaveAsExpected()
    {
        var service = new ThemeService();

        Assert.Equal(ThemeService.LightTheme, service.Normalize("light"));
        Assert.Equal(ThemeService.DarkTheme, service.Normalize("unknown"));
        Assert.True(service.IsDark(null));
        Assert.True(service.IsDark("dark"));
        Assert.False(service.IsDark("light"));
    }

    [Fact]
    public void Apply_WithoutApplicationCurrent_DoesNotThrow()
    {
        if (Application.Current is not null)
        {
            return;
        }

        var service = new ThemeService();

        var ex = Record.Exception(() => service.Apply(ThemeService.LightTheme));

        Assert.Null(ex);
    }
}
