using System.Windows;

namespace McServerManager.Services;

public sealed class ThemeService : IThemeService
{
    public const string DarkTheme = "Dark";
    public const string LightTheme = "Light";
    private const string ThemePathPrefix = "Resources/Themes/";

    public bool IsDark(string? theme)
    {
        return !string.Equals(Normalize(theme), LightTheme, StringComparison.OrdinalIgnoreCase);
    }

    public string Normalize(string? theme)
    {
        if (string.Equals(theme, LightTheme, StringComparison.OrdinalIgnoreCase))
        {
            return LightTheme;
        }

        return DarkTheme;
    }

    public void Apply(string? theme)
    {
        var app = System.Windows.Application.Current;
        if (app is null)
        {
            return;
        }

        var normalized = Normalize(theme);
        var source = new Uri($"{ThemePathPrefix}{normalized}.xaml", UriKind.Relative);
        var dictionaries = app.Resources.MergedDictionaries;
        var existing = dictionaries.FirstOrDefault(d =>
            d.Source?.OriginalString?.Contains(ThemePathPrefix, StringComparison.OrdinalIgnoreCase) == true);

        if (existing is not null)
        {
            var index = dictionaries.IndexOf(existing);
            dictionaries[index] = new ResourceDictionary { Source = source };
            return;
        }

        dictionaries.Insert(0, new ResourceDictionary { Source = source });
    }
}
