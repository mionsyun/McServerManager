using System.Diagnostics;
using System.Net.Http;
using System.Text.Json;
using System.Xml.Linq;

namespace McServerManager.Services;

public sealed class ServerJarService
{
    private const string PaperProject = "paper";
    private const string PaperApiBase = "https://api.papermc.io/v2/projects";
    private const string FabricApiBase = "https://meta.fabricmc.net/v2/versions";
    private const string PurpurApiBase = "https://api.purpurmc.org/v2/purpur";
    private const string ForgeMetadataUrl = "https://maven.minecraftforge.net/net/minecraftforge/forge/maven-metadata.xml";
    private const string SpigotBuildToolsUrl = "https://hub.spigotmc.org/jenkins/job/BuildTools/lastSuccessfulBuild/artifact/target/BuildTools.jar";
    private readonly MinecraftVersionService _versionService;
    private readonly JavaService _javaService;
    private readonly HttpClient _httpClient = new();

    public ServerJarService(MinecraftVersionService versionService, JavaService javaService)
    {
        _versionService = versionService;
        _javaService = javaService;
    }

    public Task DownloadAsync(string serverType, string versionId, string destinationPath, string? javaPath)
    {
        return serverType switch
        {
            "Forge" => DownloadForgeAsync(versionId, destinationPath, javaPath),
            "Spigot" => DownloadSpigotAsync(versionId, destinationPath, javaPath),
            "Purpur" => DownloadPurpurAsync(versionId, destinationPath),
            "Paper" => DownloadPaperAsync(versionId, destinationPath),
            "Fabric" => DownloadFabricAsync(versionId, destinationPath),
            _ => _versionService.DownloadServerJarAsync(versionId, destinationPath)
        };
    }

    private async Task DownloadPaperAsync(string versionId, string destinationPath)
    {
        var buildsUrl = $"{PaperApiBase}/{PaperProject}/versions/{versionId}";
        using var buildsResponse = await _httpClient.GetAsync(buildsUrl).ConfigureAwait(false);
        buildsResponse.EnsureSuccessStatusCode();
        var buildsJson = await buildsResponse.Content.ReadAsStringAsync().ConfigureAwait(false);
        using var buildsDoc = JsonDocument.Parse(buildsJson);

        if (!buildsDoc.RootElement.TryGetProperty("builds", out var buildsElement) || buildsElement.GetArrayLength() == 0)
        {
            throw new InvalidOperationException("Paper のビルド情報が見つかりません。");
        }

        var latestBuild = buildsElement.EnumerateArray().Select(b => b.GetInt32()).Max();
        var fileName = $"paper-{versionId}-{latestBuild}.jar";
        var downloadUrl = $"{PaperApiBase}/{PaperProject}/versions/{versionId}/builds/{latestBuild}/downloads/{fileName}";

        await DownloadFileAsync(downloadUrl, destinationPath).ConfigureAwait(false);
    }

    private async Task DownloadSpigotAsync(string versionId, string destinationPath, string? javaPath)
    {
        var javaExe = ResolveJavaPath(javaPath);
        EnsureGitAvailable();

        var serverDir = Path.GetDirectoryName(destinationPath) ?? throw new InvalidOperationException("サーバーディレクトリが見つかりません。");
        var buildToolsPath = Path.Combine(serverDir, "BuildTools.jar");
        await DownloadFileAsync(SpigotBuildToolsUrl, buildToolsPath).ConfigureAwait(false);

        await RunProcessAsync(javaExe, $"-jar \"{buildToolsPath}\" --rev {versionId}", serverDir).ConfigureAwait(false);

        var builtJar = Directory.EnumerateFiles(serverDir, $"spigot-{versionId}.jar", SearchOption.TopDirectoryOnly)
            .FirstOrDefault();
        if (string.IsNullOrWhiteSpace(builtJar))
        {
            throw new InvalidOperationException("Spigot のビルドに失敗しました。");
        }

        File.Copy(builtJar, destinationPath, true);
    }

    private async Task DownloadPurpurAsync(string versionId, string destinationPath)
    {
        var buildsUrl = $"{PurpurApiBase}/{versionId}";
        using var buildsResponse = await _httpClient.GetAsync(buildsUrl).ConfigureAwait(false);
        buildsResponse.EnsureSuccessStatusCode();
        var buildsJson = await buildsResponse.Content.ReadAsStringAsync().ConfigureAwait(false);
        using var buildsDoc = JsonDocument.Parse(buildsJson);

        if (!buildsDoc.RootElement.TryGetProperty("builds", out var buildsElement) ||
            !buildsElement.TryGetProperty("all", out var allElement) ||
            allElement.GetArrayLength() == 0)
        {
            throw new InvalidOperationException("Purpur のビルド情報が見つかりません。");
        }

        var latestBuild = allElement.EnumerateArray().Select(b => b.GetInt32()).Max();
        var downloadUrl = $"{PurpurApiBase}/{versionId}/{latestBuild}/download";

        await DownloadFileAsync(downloadUrl, destinationPath).ConfigureAwait(false);
    }

    private async Task DownloadForgeAsync(string versionId, string destinationPath, string? javaPath)
    {
        var javaExe = ResolveJavaPath(javaPath);
        var forgeVersion = await GetLatestForgeVersionAsync(versionId).ConfigureAwait(false);
        var serverDir = Path.GetDirectoryName(destinationPath) ?? throw new InvalidOperationException("サーバーディレクトリが見つかりません。");
        var installerUrl = $"https://maven.minecraftforge.net/net/minecraftforge/forge/{versionId}-{forgeVersion}/forge-{versionId}-{forgeVersion}-installer.jar";
        var installerPath = Path.Combine(serverDir, $"forge-{versionId}-{forgeVersion}-installer.jar");

        await DownloadFileAsync(installerUrl, installerPath).ConfigureAwait(false);
        await RunProcessAsync(javaExe, $"-jar \"{installerPath}\" --installServer", serverDir).ConfigureAwait(false);

        var serverJar = FindForgeServerJar(serverDir, versionId, forgeVersion);
        if (string.IsNullOrWhiteSpace(serverJar))
        {
            throw new InvalidOperationException("Forge のサーバーJarが見つかりません。");
        }

        File.Copy(serverJar, destinationPath, true);
    }

    private async Task DownloadFabricAsync(string versionId, string destinationPath)
    {
        var loaderVersion = await GetLatestFabricLoaderVersionAsync(versionId).ConfigureAwait(false);
        var installerVersion = await GetLatestFabricInstallerVersionAsync().ConfigureAwait(false);

        var downloadUrl = $"{FabricApiBase}/loader/{versionId}/{loaderVersion}/{installerVersion}/server/jar";
        await DownloadFileAsync(downloadUrl, destinationPath).ConfigureAwait(false);
    }

    private async Task<string> GetLatestFabricLoaderVersionAsync(string versionId)
    {
        var url = $"{FabricApiBase}/loader/{versionId}";
        var json = await _httpClient.GetStringAsync(url).ConfigureAwait(false);
        using var doc = JsonDocument.Parse(json);
        foreach (var item in doc.RootElement.EnumerateArray())
        {
            var stable = item.GetProperty("loader").GetProperty("stable").GetBoolean();
            var loaderVersion = item.GetProperty("loader").GetProperty("version").GetString();
            if (stable && !string.IsNullOrWhiteSpace(loaderVersion))
            {
                return loaderVersion;
            }
        }

        var fallback = doc.RootElement.EnumerateArray()
            .Select(item => item.GetProperty("loader").GetProperty("version").GetString())
            .FirstOrDefault(value => !string.IsNullOrWhiteSpace(value));

        return fallback ?? throw new InvalidOperationException("Fabric Loader のバージョンが取得できませんでした。");
    }

    private async Task<string> GetLatestFabricInstallerVersionAsync()
    {
        var url = $"{FabricApiBase}/installer";
        var json = await _httpClient.GetStringAsync(url).ConfigureAwait(false);
        using var doc = JsonDocument.Parse(json);
        foreach (var item in doc.RootElement.EnumerateArray())
        {
            var stable = item.GetProperty("stable").GetBoolean();
            var version = item.GetProperty("version").GetString();
            if (stable && !string.IsNullOrWhiteSpace(version))
            {
                return version;
            }
        }

        var fallback = doc.RootElement.EnumerateArray()
            .Select(item => item.GetProperty("version").GetString())
            .FirstOrDefault(value => !string.IsNullOrWhiteSpace(value));

        return fallback ?? throw new InvalidOperationException("Fabric Installer のバージョンが取得できませんでした。");
    }

    private async Task<string> GetLatestForgeVersionAsync(string versionId)
    {
        var xml = await _httpClient.GetStringAsync(ForgeMetadataUrl).ConfigureAwait(false);
        var doc = XDocument.Parse(xml);
        var versions = doc.Descendants("version")
            .Select(element => element.Value)
            .Where(value => value.StartsWith($"{versionId}-", StringComparison.OrdinalIgnoreCase))
            .Select(value => value.Split('-', 2)[1])
            .ToList();

        if (versions.Count == 0)
        {
            throw new InvalidOperationException("Forge のバージョンが取得できませんでした。");
        }

        versions.Sort(CompareForgeVersions);
        return versions.Last();
    }

    private static int CompareForgeVersions(string left, string right)
    {
        var leftParts = left.Split('.', StringSplitOptions.RemoveEmptyEntries);
        var rightParts = right.Split('.', StringSplitOptions.RemoveEmptyEntries);
        var length = Math.Max(leftParts.Length, rightParts.Length);
        for (var i = 0; i < length; i++)
        {
            var l = i < leftParts.Length && int.TryParse(leftParts[i], out var li) ? li : 0;
            var r = i < rightParts.Length && int.TryParse(rightParts[i], out var ri) ? ri : 0;
            var compare = l.CompareTo(r);
            if (compare != 0)
            {
                return compare;
            }
        }

        return 0;
    }

    private static string? FindForgeServerJar(string serverDir, string mcVersion, string forgeVersion)
    {
        var candidates = Directory.EnumerateFiles(serverDir, "forge-*.jar", SearchOption.TopDirectoryOnly)
            .Where(path => !path.EndsWith("-installer.jar", StringComparison.OrdinalIgnoreCase))
            .ToList();

        var serverJar = candidates.FirstOrDefault(path => path.EndsWith("-server.jar", StringComparison.OrdinalIgnoreCase));
        if (!string.IsNullOrWhiteSpace(serverJar))
        {
            return serverJar;
        }

        var universalJar = candidates.FirstOrDefault(path => path.EndsWith("-universal.jar", StringComparison.OrdinalIgnoreCase));
        if (!string.IsNullOrWhiteSpace(universalJar))
        {
            return universalJar;
        }

        return candidates.FirstOrDefault(path =>
            path.Contains($"{mcVersion}-{forgeVersion}", StringComparison.OrdinalIgnoreCase));
    }

    private string ResolveJavaPath(string? javaPath)
    {
        if (!string.IsNullOrWhiteSpace(javaPath) && File.Exists(javaPath))
        {
            return javaPath;
        }

        var detected = _javaService.FindJavaExecutable();
        if (!string.IsNullOrWhiteSpace(detected))
        {
            return detected;
        }

        throw new InvalidOperationException("Java が見つかりません。設定で java.exe を指定してください。");
    }

    private static void EnsureGitAvailable()
    {
        try
        {
            var info = new ProcessStartInfo
            {
                FileName = "git",
                Arguments = "--version",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var process = Process.Start(info);
            if (process is null)
            {
                throw new InvalidOperationException("Git が見つかりません。Spigot のビルドには Git が必要です。");
            }

            process.WaitForExit(1000);
            if (process.ExitCode != 0)
            {
                throw new InvalidOperationException("Git が見つかりません。Spigot のビルドには Git が必要です。");
            }
        }
        catch (InvalidOperationException)
        {
            throw;
        }
        catch
        {
            throw new InvalidOperationException("Git が見つかりません。Spigot のビルドには Git が必要です。");
        }
    }

    private async Task DownloadFileAsync(string url, string destinationPath)
    {
        using var response = await _httpClient.GetAsync(url).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        await using var output = File.Create(destinationPath);
        await response.Content.CopyToAsync(output).ConfigureAwait(false);
    }

    private static async Task RunProcessAsync(string fileName, string arguments, string workingDirectory)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = fileName,
            Arguments = arguments,
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        using var process = Process.Start(startInfo);
        if (process is null)
        {
            throw new InvalidOperationException("プロセスの起動に失敗しました。");
        }

        var stderr = await process.StandardError.ReadToEndAsync().ConfigureAwait(false);
        await process.WaitForExitAsync().ConfigureAwait(false);
        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException($"実行に失敗しました: {stderr}");
        }
    }
}
