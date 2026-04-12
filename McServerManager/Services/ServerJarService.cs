using System.Diagnostics;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace McServerManager.Services;

public sealed class ServerJarService : IServerJarService
{
    private const string PaperProject = "paper";
    private static readonly Regex ChecksumRegex = new(@"\b(?<hash>[A-Fa-f0-9]{40}|[A-Fa-f0-9]{64})\b", RegexOptions.Compiled);
    private readonly IMinecraftVersionService _versionService;
    private readonly IJavaService _javaService;
    private readonly HttpClient _httpClient = new();

    public ServerJarService(IMinecraftVersionService versionService, IJavaService javaService)
    {
        _versionService = versionService;
        _javaService = javaService;
        _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("McServerManager/1.0 (+https://github.com)");
        _httpClient.DefaultRequestHeaders.Accept.ParseAdd("*/*");
    }

    public Task DownloadAsync(string serverType, string versionId, string destinationPath, string? javaPath, IProgress<string>? progress = null)
    {
        progress?.Report($"Downloading server software: {serverType} {versionId}");

        return serverType switch
        {
            "Forge" => DownloadForgeAsync(versionId, destinationPath, javaPath, progress),
            "Spigot" => DownloadSpigotAsync(versionId, destinationPath, progress),
            "Purpur" => DownloadPurpurAsync(versionId, destinationPath, progress),
            "Paper" => DownloadPaperAsync(versionId, destinationPath, progress),
            "Fabric" => DownloadFabricAsync(versionId, destinationPath, progress),
            _ => DownloadVanillaAsync(versionId, destinationPath, progress)
        };
    }

    private async Task DownloadVanillaAsync(string versionId, string destinationPath, IProgress<string>? progress)
    {
        progress?.Report("Downloading Vanilla server.jar...");
        await _versionService.DownloadServerJarAsync(versionId, destinationPath).ConfigureAwait(false);
    }

    private async Task DownloadPaperAsync(string versionId, string destinationPath, IProgress<string>? progress)
    {
        progress?.Report("Fetching Paper build information...");
        var buildsUrl = $"{ExternalApiUrls.PaperApiBase}/{PaperProject}/versions/{versionId}";
        using var buildsResponse = await _httpClient.GetAsync(buildsUrl).ConfigureAwait(false);
        buildsResponse.EnsureSuccessStatusCode();
        var buildsJson = await buildsResponse.Content.ReadAsStringAsync().ConfigureAwait(false);
        using var buildsDoc = JsonDocument.Parse(buildsJson);

        if (!buildsDoc.RootElement.TryGetProperty("builds", out var buildsElement) || buildsElement.GetArrayLength() == 0)
        {
            throw new InvalidOperationException("Paper build information was not found.");
        }

        var latestBuild = buildsElement.EnumerateArray().Select(b => b.GetInt32()).Max();
        var fileName = $"paper-{versionId}-{latestBuild}.jar";
        var downloadUrl = $"{ExternalApiUrls.PaperApiBase}/{PaperProject}/versions/{versionId}/builds/{latestBuild}/downloads/{fileName}";

        progress?.Report("Downloading Paper...");
        await DownloadFileAsync(downloadUrl, destinationPath).ConfigureAwait(false);
    }

    private async Task DownloadSpigotAsync(string versionId, string destinationPath, IProgress<string>? progress)
    {
        progress?.Report("Trying direct Spigot download...");

        HttpRequestException? lastHttpError = null;
        var endpoints = new[]
        {
            string.Format(ExternalApiUrls.SpigotDirectJarTemplate, versionId),
            string.Format(ExternalApiUrls.SpigotDirectJarFallbackTemplate, versionId)
        };

        for (var i = 0; i < endpoints.Length; i++)
        {
            var endpoint = endpoints[i];
            try
            {
                await DownloadSpigotDirectJarAsync(endpoint, destinationPath, progress).ConfigureAwait(false);
                return;
            }
            catch (HttpRequestException ex)
            {
                lastHttpError = ex;
                if (i < endpoints.Length - 1)
                {
                    progress?.Report("Primary Spigot endpoint failed. Trying fallback endpoint...");
                }
            }
        }

        throw new InvalidOperationException(
            "Spigot 直リンクのダウンロードに失敗しました。セキュリティ対策として BuildTools の自動実行は無効化されています。手動で BuildTools を実行して生成した jar を配置してください。",
            lastHttpError);
    }

    private async Task DownloadSpigotDirectJarAsync(string url, string destinationPath, IProgress<string>? progress)
    {
        progress?.Report($"Downloading Spigot directly: {url}");

        // Propagate HttpRequestException and handle policy at caller.
        await DownloadFileAsync(url, destinationPath).ConfigureAwait(false);
    }

    private async Task DownloadPurpurAsync(string versionId, string destinationPath, IProgress<string>? progress)
    {
        progress?.Report("Fetching Purpur build information...");
        var buildsUrl = $"{ExternalApiUrls.PurpurApiBase}/{versionId}";
        using var buildsResponse = await _httpClient.GetAsync(buildsUrl).ConfigureAwait(false);
        buildsResponse.EnsureSuccessStatusCode();
        var buildsJson = await buildsResponse.Content.ReadAsStringAsync().ConfigureAwait(false);
        using var buildsDoc = JsonDocument.Parse(buildsJson);

        if (!buildsDoc.RootElement.TryGetProperty("builds", out var buildsElement) ||
            !buildsElement.TryGetProperty("all", out var allElement) ||
            allElement.GetArrayLength() == 0)
        {
            throw new InvalidOperationException("Purpur build information was not found.");
        }

        // Purpur API may return build number as either number or string.
        var latestBuild = allElement.EnumerateArray()
            .Select(b => b.ValueKind == JsonValueKind.Number ? b.GetInt32() : int.Parse(b.GetString()!))
            .Max();
        var downloadUrl = $"{ExternalApiUrls.PurpurApiBase}/{versionId}/{latestBuild}/download";

        progress?.Report("Downloading Purpur...");
        await DownloadFileAsync(downloadUrl, destinationPath).ConfigureAwait(false);
    }

    private async Task DownloadForgeAsync(string versionId, string destinationPath, string? javaPath, IProgress<string>? progress)
    {
        progress?.Report("Fetching Forge installer information...");
        var javaExe = ResolveJavaPath(javaPath);
        var forgeVersion = await GetLatestForgeVersionAsync(versionId).ConfigureAwait(false);
        var serverDir = Path.GetDirectoryName(destinationPath) ?? throw new InvalidOperationException("Destination directory could not be resolved.");
        var installerUrl = $"{ExternalApiUrls.ForgeMavenBase}/net/minecraftforge/forge/{versionId}-{forgeVersion}/forge-{versionId}-{forgeVersion}-installer.jar";
        var installerPath = Path.Combine(serverDir, $"forge-{versionId}-{forgeVersion}-installer.jar");

        progress?.Report("Downloading Forge installer...");
        await DownloadFileAsync(installerUrl, installerPath).ConfigureAwait(false);
        progress?.Report("Verifying Forge installer checksum...");
        await VerifyDownloadedFileChecksumAsync(installerUrl, installerPath).ConfigureAwait(false);

        if (!File.Exists(installerPath))
        {
            throw new InvalidOperationException("Failed to download Forge installer.");
        }

        progress?.Report("Running Forge installer (this may take a while)...");
        await RunProcessAsync(javaExe, $"-jar \"{installerPath}\" --installServer", serverDir, progress).ConfigureAwait(false);

        // Forge 1.17+ typically provides run scripts and stores jars under libraries/.
        // First, try traditional jar patterns.
        var serverJar = FindForgeServerJar(serverDir, versionId, forgeVersion);
        if (!string.IsNullOrWhiteSpace(serverJar))
        {
            if (serverJar != destinationPath)
            {
                File.Copy(serverJar, destinationPath, true);
            }
            progress?.Report("Forge setup completed.");
            return;
        }

        // Forge 1.17+ format: check run.bat / run.sh presence.
        var runBat = Path.Combine(serverDir, "run.bat");
        var runSh = Path.Combine(serverDir, "run.sh");
        var userJvmArgs = Path.Combine(serverDir, "user_jvm_args.txt");

        if (File.Exists(runBat) || File.Exists(runSh))
        {
            // In 1.17+ layout, server.jar is often not used directly by run scripts.
            // Keep compatibility with the app startup flow by placing a compatible jar if available.
            if (!File.Exists(destinationPath))
            {
                // Create user_jvm_args.txt if missing.
                if (!File.Exists(userJvmArgs))
                {
                    await File.WriteAllTextAsync(userJvmArgs, "# Xmx and Xms are set by the launcher\n").ConfigureAwait(false);
                }

                // Try to locate Forge main jar in libraries and copy it.
                var forgeJarInLibs = FindForgeLibraryJar(serverDir, versionId, forgeVersion);
                if (!string.IsNullOrWhiteSpace(forgeJarInLibs))
                {
                    File.Copy(forgeJarInLibs, destinationPath, true);
                }
                else
                {
                    // Fallback marker file. Actual launch remains via run.bat/run.sh.
                    await File.WriteAllTextAsync(destinationPath, "").ConfigureAwait(false);
                }
            }
            progress?.Report("Forge setup completed (1.17+ layout).");
            return;
        }

        throw new InvalidOperationException(
            "Forge setup files were not found after installer execution.\n" +
            "Installer may have failed or generated an unexpected layout.\n" +
            $"Server directory: {serverDir}");
    }

    private async Task DownloadFabricAsync(string versionId, string destinationPath, IProgress<string>? progress)
    {
        progress?.Report("Fetching Fabric Loader information...");
        var loaderVersion = await GetLatestFabricLoaderVersionAsync(versionId).ConfigureAwait(false);
        var installerVersion = await GetLatestFabricInstallerVersionAsync().ConfigureAwait(false);

        var downloadUrl = $"{ExternalApiUrls.FabricApiBase}/loader/{versionId}/{loaderVersion}/{installerVersion}/server/jar";
        progress?.Report("Downloading Fabric server...");
        await DownloadFileAsync(downloadUrl, destinationPath).ConfigureAwait(false);
    }

    private async Task<string> GetLatestFabricLoaderVersionAsync(string versionId)
    {
        var url = $"{ExternalApiUrls.FabricApiBase}/loader/{versionId}";
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

        return fallback ?? throw new InvalidOperationException("Fabric loader version could not be determined.");
    }

    private async Task<string> GetLatestFabricInstallerVersionAsync()
    {
        var url = $"{ExternalApiUrls.FabricApiBase}/installer";
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

        return fallback ?? throw new InvalidOperationException("Fabric installer version could not be determined.");
    }

    private async Task<string> GetLatestForgeVersionAsync(string versionId)
    {
        var xml = await _httpClient.GetStringAsync(ExternalApiUrls.ForgeMavenMetadata).ConfigureAwait(false);
        var doc = XDocument.Parse(xml);
        var versions = doc.Descendants("version")
            .Select(element => element.Value)
            .Where(value => value.StartsWith($"{versionId}-", StringComparison.OrdinalIgnoreCase))
            .Select(value => value.Split('-', 2)[1])
            .ToList();

        if (versions.Count == 0)
        {
            throw new InvalidOperationException("Forge version metadata was not found.");
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

        // shim jar (some 1.17+ versions)
        var shimJar = candidates.FirstOrDefault(path => path.EndsWith("-shim.jar", StringComparison.OrdinalIgnoreCase));
        if (!string.IsNullOrWhiteSpace(shimJar))
        {
            return shimJar;
        }

        return candidates.FirstOrDefault(path =>
            path.Contains($"{mcVersion}-{forgeVersion}", StringComparison.OrdinalIgnoreCase));
    }

    private static string? FindForgeLibraryJar(string serverDir, string mcVersion, string forgeVersion)
    {
        var librariesDir = Path.Combine(serverDir, "libraries", "net", "minecraftforge", "forge");
        if (!Directory.Exists(librariesDir))
        {
            return null;
        }

        return Directory.EnumerateFiles(librariesDir, "*.jar", SearchOption.AllDirectories)
            .FirstOrDefault(path => path.Contains($"{mcVersion}-{forgeVersion}", StringComparison.OrdinalIgnoreCase)
                                    && !path.EndsWith("-installer.jar", StringComparison.OrdinalIgnoreCase));
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

        throw new InvalidOperationException("Java executable was not found. Configure java.exe first.");
    }

    private async Task DownloadFileAsync(string url, string destinationPath)
    {
        var destinationDirectory = Path.GetDirectoryName(destinationPath);
        if (string.IsNullOrWhiteSpace(destinationDirectory))
        {
            throw new InvalidOperationException("Destination directory is invalid.");
        }

        Directory.CreateDirectory(destinationDirectory);

        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        using var response = await _httpClient.SendAsync(request).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        await using var output = File.Create(destinationPath);
        await response.Content.CopyToAsync(output).ConfigureAwait(false);
    }

    private async Task VerifyDownloadedFileChecksumAsync(string artifactUrl, string downloadedPath)
    {
        var (expectedHash, algorithm) = await GetExpectedChecksumAsync(artifactUrl).ConfigureAwait(false);
        var actualHash = await ComputeFileHashAsync(downloadedPath, algorithm).ConfigureAwait(false);

        if (!string.Equals(expectedHash, actualHash, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"ダウンロードファイルのチェックサムが一致しません。expected={expectedHash}, actual={actualHash}, algorithm={algorithm.Name}");
        }
    }

    private async Task<(string Hash, HashAlgorithmName Algorithm)> GetExpectedChecksumAsync(string artifactUrl)
    {
        var candidates = new (string Suffix, int Length, HashAlgorithmName Algorithm)[]
        {
            (".sha256", 64, HashAlgorithmName.SHA256),
            (".sha1", 40, HashAlgorithmName.SHA1)
        };

        foreach (var candidate in candidates)
        {
            var checksumUrl = artifactUrl + candidate.Suffix;
            try
            {
                using var response = await _httpClient.GetAsync(checksumUrl).ConfigureAwait(false);
                if (!response.IsSuccessStatusCode)
                {
                    continue;
                }

                var text = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                var hash = ExtractHash(text, candidate.Length);
                if (!string.IsNullOrWhiteSpace(hash))
                {
                    return (hash, candidate.Algorithm);
                }
            }
            catch
            {
                // Try next checksum format.
            }
        }

        throw new InvalidOperationException("チェックサム情報 (.sha256/.sha1) を取得できなかったため、実行を中止しました。");
    }

    private static string ExtractHash(string text, int requiredLength)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return string.Empty;
        }

        foreach (Match match in ChecksumRegex.Matches(text))
        {
            var hash = match.Groups["hash"].Value;
            if (hash.Length == requiredLength)
            {
                return hash.ToLowerInvariant();
            }
        }

        return string.Empty;
    }

    private static async Task<string> ComputeFileHashAsync(string path, HashAlgorithmName algorithm)
    {
        await using var stream = File.OpenRead(path);
        using HashAlgorithm hasher = algorithm.Name switch
        {
            nameof(HashAlgorithmName.SHA256) => SHA256.Create(),
            nameof(HashAlgorithmName.SHA1) => SHA1.Create(),
            _ => throw new InvalidOperationException($"未対応のハッシュアルゴリズムです: {algorithm.Name}")
        };

        var hash = await hasher.ComputeHashAsync(stream).ConfigureAwait(false);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    private static Task RunProcessAsync(string fileName, string arguments, string workingDirectory)
        => RunProcessAsync(fileName, arguments, workingDirectory, null);

    private static async Task RunProcessAsync(string fileName, string arguments, string workingDirectory, IProgress<string>? progress)
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
            throw new InvalidOperationException("Failed to start process.");
        }

        var stderrBuilder = new System.Text.StringBuilder();

        // If progress is available, stream stdout line-by-line in near real-time.
        if (progress is not null)
        {
            var stdoutLineTask = Task.Run(async () =>
            {
                while (await process.StandardOutput.ReadLineAsync().ConfigureAwait(false) is { } line)
                {
                    progress.Report(line);
                }
            });
            var stderrLineTask = Task.Run(async () =>
            {
                while (await process.StandardError.ReadLineAsync().ConfigureAwait(false) is { } line)
                {
                    stderrBuilder.AppendLine(line);
                }
            });

            await Task.WhenAll(stdoutLineTask, stderrLineTask).ConfigureAwait(false);
            await process.WaitForExitAsync().ConfigureAwait(false);

            if (process.ExitCode != 0)
            {
                var detail = stderrBuilder.ToString();
                if (detail.Length > 500) detail = "..." + detail[^500..];
                throw new InvalidOperationException($"Process exited with code {process.ExitCode}:\n{detail}");
            }
        }
        else
        {
            // Without progress callback, read stdout/stderr in bulk.
            var stdoutTask = process.StandardOutput.ReadToEndAsync();
            var stderrTask = process.StandardError.ReadToEndAsync();

            await Task.WhenAll(stdoutTask, stderrTask).ConfigureAwait(false);
            await process.WaitForExitAsync().ConfigureAwait(false);

            if (process.ExitCode != 0)
            {
                var stderr = await stderrTask.ConfigureAwait(false);
                var stdout = await stdoutTask.ConfigureAwait(false);
                var detail = !string.IsNullOrWhiteSpace(stderr) ? stderr : stdout;
                if (detail.Length > 500) detail = "..." + detail[^500..];
                throw new InvalidOperationException($"Process exited with code {process.ExitCode}:\n{detail}");
            }
        }
    }
}

