using System.Diagnostics;
using System.Text.RegularExpressions;

namespace McServerManager.Services;

public sealed class JavaService : IJavaService
{
    private static readonly Regex JavaVersionRegex = new(@"version\s+\""?([0-9]+(?:\.[0-9]+)*)", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public string? FindJavaExecutable()
    {
        var fromWhere = TryFindJavaFromWhere();
        if (!string.IsNullOrWhiteSpace(fromWhere))
        {
            return fromWhere;
        }

        var javaHome = Environment.GetEnvironmentVariable("JAVA_HOME");
        if (!string.IsNullOrWhiteSpace(javaHome))
        {
            var javaPath = Path.Combine(javaHome, "bin", "java.exe");
            if (File.Exists(javaPath))
            {
                return javaPath;
            }
        }

        var programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        var programFilesX86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
        foreach (var root in new[] { programFiles, programFilesX86 })
        {
            if (string.IsNullOrWhiteSpace(root))
            {
                continue;
            }

            var javaDir = Path.Combine(root, "Java");
            if (!Directory.Exists(javaDir))
            {
                continue;
            }

            var candidate = Directory.EnumerateFiles(javaDir, "java.exe", SearchOption.AllDirectories)
                .FirstOrDefault();
            if (!string.IsNullOrWhiteSpace(candidate))
            {
                return candidate;
            }
        }

        return null;
    }

    public bool TryGetJavaMajorVersion(string javaExe, out int major, out string? rawVersion)
    {
        major = 0;
        rawVersion = null;

        try
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = javaExe,
                Arguments = "-version",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var process = Process.Start(startInfo);
            if (process is null)
            {
                return false;
            }

            var output = process.StandardOutput.ReadToEnd();
            var error = process.StandardError.ReadToEnd();
            process.WaitForExit(2000);

            var text = string.Join(Environment.NewLine, output, error);
            var match = JavaVersionRegex.Match(text);
            if (!match.Success)
            {
                return false;
            }

            rawVersion = match.Groups[1].Value;
            return TryParseJavaMajor(rawVersion, out major);
        }
        catch
        {
            return false;
        }
    }

    public int? GetRequiredJavaMajor(string? minecraftVersion)
    {
        if (string.IsNullOrWhiteSpace(minecraftVersion))
        {
            return null;
        }

        if (!TryParseMinecraftVersion(minecraftVersion, out var minor, out var patch))
        {
            return null;
        }

        if (minor > 20 || (minor == 20 && patch >= 5))
        {
            return 21;
        }

        if (minor >= 18)
        {
            return 17;
        }

        if (minor == 17)
        {
            return 16;
        }

        return 8;
    }

    private static string? TryFindJavaFromWhere()
    {
        try
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = "where",
                Arguments = "java",
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var process = Process.Start(startInfo);
            if (process is null)
            {
                return null;
            }

            var output = process.StandardOutput.ReadToEnd();
            process.WaitForExit(1000);
            var line = output.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
            return string.IsNullOrWhiteSpace(line) ? null : line.Trim();
        }
        catch
        {
            return null;
        }
    }

    private static bool TryParseJavaMajor(string rawVersion, out int major)
    {
        major = 0;
        var parts = rawVersion.Split('.');
        if (parts.Length == 0)
        {
            return false;
        }

        if (parts[0] == "1" && parts.Length >= 2 && int.TryParse(parts[1], out var legacyMajor))
        {
            major = legacyMajor;
            return true;
        }

        return int.TryParse(parts[0], out major);
    }

    private static bool TryParseMinecraftVersion(string version, out int minor, out int patch)
    {
        minor = 0;
        patch = 0;

        var main = version.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries)[0];
        var parts = main.Split('.');
        if (parts.Length < 2)
        {
            return false;
        }

        if (parts[0] != "1")
        {
            return false;
        }

        if (!int.TryParse(parts[1], out minor))
        {
            return false;
        }

        if (parts.Length >= 3)
        {
            int.TryParse(parts[2], out patch);
        }

        return true;
    }
}
