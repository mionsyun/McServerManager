namespace McServerManager.Services;

public interface IJavaService
{
    string? FindJavaExecutable();
    bool TryGetJavaMajorVersion(string javaExe, out int major, out string? rawVersion);
    int? GetRequiredJavaMajor(string? minecraftVersion);
}
