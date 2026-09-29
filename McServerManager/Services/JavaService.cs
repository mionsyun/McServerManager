using System.Diagnostics;
using System.Text.RegularExpressions;

namespace McServerManager.Services;

public sealed class JavaService : IJavaService
{
    private static readonly Regex JavaVersionRegex = new(@"version\s+\""?([0-9]+(?:\.[0-9]+)*)", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex ReleaseVersionRegex = new(@"^JAVA_VERSION=""?([0-9._]+)", RegexOptions.Multiline | RegexOptions.Compiled);

    /// <summary>年ベースの版番号 (26.1 など) になった最初の年。以降は Java 25 が必要。</summary>
    private const int FirstYearBasedMinecraftVersion = 26;

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

        return FindNewestJava(GetVendorInstallRoots())
               ?? FindNewestJava(GetMinecraftLauncherRuntimeHomes());
    }

    /// <summary>
    /// 各配布元のインストール先フォルダ（例: C:\Program Files\Eclipse Adoptium）直下の JDK/JRE から
    /// java.exe を探し、最も新しいバージョンのものを返す。
    /// </summary>
    public static string? FindNewestJava(IEnumerable<string> installRoots)
    {
        var candidates = new List<(string JavaExe, Version Version)>();
        foreach (var root in installRoots.Where(Directory.Exists))
        {
            IEnumerable<string> homes;
            try
            {
                homes = Directory.EnumerateDirectories(root).Prepend(root).ToList();
            }
            catch (IOException) { continue; }
            catch (UnauthorizedAccessException) { continue; }

            foreach (var home in homes)
            {
                var javaExe = Path.Combine(home, "bin", "java.exe");
                if (File.Exists(javaExe))
                {
                    candidates.Add((javaExe, ReadJavaHomeVersion(home)));
                }
            }
        }

        return candidates
            .OrderByDescending(c => c.Version)
            .Select(c => c.JavaExe)
            .FirstOrDefault();
    }

    private static IEnumerable<string> GetVendorInstallRoots()
    {
        // 主要な OpenJDK 配布元の既定インストール先（配布元フォルダ直下に jdk-21.0.x などが入る）
        string[] vendors =
        [
            "Java", "Eclipse Adoptium", "Eclipse Foundation", "AdoptOpenJDK", "Microsoft", "Zulu",
            "Amazon Corretto", "BellSoft", "Semeru", "OpenJDK", "ojdkbuild"
        ];
        string[] bases =
        [
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs")
        ];

        return bases
            .Where(b => !string.IsNullOrWhiteSpace(b))
            .SelectMany(b => vendors.Select(v => Path.Combine(b, v)));
    }

    /// <summary>Minecraft Launcher が同梱する Java（runtime\java-runtime-xxx\windows-x64\java-runtime-xxx）。</summary>
    private static IEnumerable<string> GetMinecraftLauncherRuntimeHomes()
    {
        string[] runtimeRoots =
        [
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Packages", "Microsoft.4297127D64EC6_8wekyb3d8bbwe", "LocalCache", "Local", "runtime"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Minecraft Launcher", "runtime")
        ];

        foreach (var runtimeRoot in runtimeRoots.Where(Directory.Exists))
        {
            List<string> platformDirs;
            try
            {
                platformDirs = Directory.EnumerateDirectories(runtimeRoot)
                    .Select(component => Path.Combine(component, "windows-x64"))
                    .Where(Directory.Exists)
                    .ToList();
            }
            catch (IOException) { continue; }
            catch (UnauthorizedAccessException) { continue; }

            foreach (var platformDir in platformDirs)
            {
                yield return platformDir;
            }
        }
    }

    /// <summary>JDK/JRE 直下の release ファイル（JAVA_VERSION="21.0.2"）からバージョンを読む。読めなければ 0。</summary>
    private static Version ReadJavaHomeVersion(string javaHome)
    {
        try
        {
            var releaseFile = Path.Combine(javaHome, "release");
            if (File.Exists(releaseFile))
            {
                var match = ReleaseVersionRegex.Match(File.ReadAllText(releaseFile));
                if (match.Success)
                {
                    var numbers = match.Groups[1].Value.Split('.', '_').Take(3).ToList();
                    // Java 8 は "1.8.0_392" 形式
                    if (numbers.Count >= 2 && numbers[0] == "1")
                    {
                        numbers.RemoveAt(0);
                    }

                    while (numbers.Count < 2)
                    {
                        numbers.Add("0");
                    }

                    if (Version.TryParse(string.Join('.', numbers), out var version))
                    {
                        return version;
                    }
                }
            }
        }
        catch (IOException)
        {
            // 読めない release ファイルは不明扱い
        }

        return new Version(0, 0);
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

        // 26.1 以降は「年.回」形式 (26.3, 26.1-snapshot-2 など) で、Java 25 が必要
        var leading = minecraftVersion.Trim().Split('.', '-', ' ')[0];
        if (int.TryParse(leading, out var year) && year >= FirstYearBasedMinecraftVersion)
        {
            return 25;
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
