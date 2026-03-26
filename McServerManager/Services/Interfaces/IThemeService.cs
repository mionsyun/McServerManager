namespace McServerManager.Services;

public interface IThemeService
{
    bool IsDark(string? theme);
    string Normalize(string? theme);
    void Apply(string? theme);
}
