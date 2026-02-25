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
    private const string SpigotDirectJarUrlTemplate = "https://download.getbukkit.org/spigot/spigot-{0}.jar";
    private readonly MinecraftVersionService _versionService;
    private readonly JavaService _javaService;
    private readonly HttpClient _httpClient = new();

    public ServerJarService(MinecraftVersionService versionService, JavaService javaService)
    {
        _versionService = versionService;
        _javaService = javaService;
        _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("McServerManager/1.0 (+https://github.com)");
        _httpClient.DefaultRequestHeaders.Accept.ParseAdd("*/*");
    }

    public Task DownloadAsync(string serverType, string versionId, string destinationPath, string? javaPath, IProgress<string>? progress = null)
    {
        progress?.Report($"サーバーソフトを取得中: {serverType} {versionId}");

        return serverType switch
        {
            "Forge" => DownloadForgeAsync(versionId, destinationPath, javaPath, progress),
            "Spigot" => DownloadSpigotAsync(versionId, destinationPath, javaPath, progress),
            "Purpur" => DownloadPurpurAsync(versionId, destinationPath, progress),
            "Paper" => DownloadPaperAsync(versionId, destinationPath, progress),
            "Fabric" => DownloadFabricAsync(versionId, destinationPath, progress),
            _ => DownloadVanillaAsync(versionId, destinationPath, progress)
        };
    }

    private async Task DownloadVanillaAsync(string versionId, string destinationPath, IProgress<string>? progress)
    {
        progress?.Report("Vanilla server.jar をダウンロードしています...");
        await _versionService.DownloadServerJarAsync(versionId, destinationPath).ConfigureAwait(false);
    }

    private async Task DownloadPaperAsync(string versionId, string destinationPath, IProgress<string>? progress)
    {
        progress?.Report("Paper のビルド情報を取得しています...");
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

        progress?.Report("Paper をダウンロードしています...");
        await DownloadFileAsync(downloadUrl, destinationPath).ConfigureAwait(false);
    }

    private async Task DownloadSpigotAsync(string versionId, string destinationPath, string? javaPath, IProgress<string>? progress)
    {
        progress?.Report("Spigot 本体の直接ダウンロードを試行しています...");
        try
        {
            await DownloadSpigotDirectJarAsync(versionId, destinationPath, progress).ConfigureAwait(false);
        }
        catch (HttpRequestException)
        {
            progress?.Report("直接ダウンロードに失敗したため、BuildTools ビルドに切り替えます...");

            var javaExe = ResolveJavaPath(javaPath);
            EnsureGitAvailable();

            var serverDir = Path.GetDirectoryName(destinationPath) ?? throw new InvalidOperationException("サーバーディレクトリが見つかりません。");
            var buildToolsPath = Path.Combine(serverDir, "BuildTools.jar");

            progress?.Report("BuildTools をダウンロードしています...");
            await DownloadFileAsync(SpigotBuildToolsUrl, buildToolsPath).ConfigureAwait(false);

            progress?.Report("Spigot をビルドしています（数分かかる場合があります）...");
            await RunProcessAsync(javaExe, $"-jar \"{buildToolsPath}\" --rev {versionId}", serverDir, progress).ConfigureAwait(false);

            var builtJar = Directory.EnumerateFiles(serverDir, $"spigot-{versionId}.jar", SearchOption.TopDirectoryOnly)
                .FirstOrDefault();
            if (string.IsNullOrWhiteSpace(builtJar))
            {
                throw new InvalidOperationException("Spigot のビルドに失敗しました。");
            }

            File.Copy(builtJar, destinationPath, true);
        }
    }

    private async Task DownloadSpigotDirectJarAsync(string versionId, string destinationPath, IProgress<string>? progress)
    {
        var directUrl = string.Format(SpigotDirectJarUrlTemplate, versionId);
        progress?.Report("Spigot 本体を直接ダウンロードしています...");

        // HttpRequestException をそのまま伝播させ、呼び出し元で BuildTools フォールバックを行う
        await DownloadFileAsync(directUrl, destinationPath).ConfigureAwait(false);
    }

    private async Task DownloadPurpurAsync(string versionId, string destinationPath, IProgress<string>? progress)
    {
        progress?.Report("Purpur のビルド情報を取得しています...");
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

        // Purpur API はビルド番号を文字列で返すため GetString → int.Parse で取得
        var latestBuild = allElement.EnumerateArray()
            .Select(b => b.ValueKind == JsonValueKind.Number ? b.GetInt32() : int.Parse(b.GetString()!))
            .Max();
        var downloadUrl = $"{PurpurApiBase}/{versionId}/{latestBuild}/download";

        progress?.Report("Purpur をダウンロードしています...");
        await DownloadFileAsync(downloadUrl, destinationPath).ConfigureAwait(false);
    }

    private async Task DownloadForgeAsync(string versionId, string destinationPath, string? javaPath, IProgress<string>? progress)
    {
        progress?.Report("Forge のインストーラー情報を取得しています...");
        var javaExe = ResolveJavaPath(javaPath);
        var forgeVersion = await GetLatestForgeVersionAsync(versionId).ConfigureAwait(false);
        var serverDir = Path.GetDirectoryName(destinationPath) ?? throw new InvalidOperationException("サーバーディレクトリが見つかりません。");
        var installerUrl = $"https://maven.minecraftforge.net/net/minecraftforge/forge/{versionId}-{forgeVersion}/forge-{versionId}-{forgeVersion}-installer.jar";
        var installerPath = Path.Combine(serverDir, $"forge-{versionId}-{forgeVersion}-installer.jar");

        progress?.Report("Forge インストーラーをダウンロードしています...");
        await DownloadFileAsync(installerUrl, installerPath).ConfigureAwait(false);

        if (!File.Exists(installerPath))
        {
            throw new InvalidOperationException("Forge インストーラーのダウンロードに失敗しました。");
        }

        progress?.Report("Forge サーバーをセットアップしています（数分かかる場合があります）...");
        await RunProcessAsync(javaExe, $"-jar \"{installerPath}\" --installServer", serverDir, progress).ConfigureAwait(false);

        // Forge 1.17+ は run.sh/run.bat を生成し、jarは libraries/ 以下に配置される
        // まず従来形式（1.16以前）の jar を探す
        var serverJar = FindForgeServerJar(serverDir, versionId, forgeVersion);
        if (!string.IsNullOrWhiteSpace(serverJar))
        {
            if (serverJar != destinationPath)
            {
                File.Copy(serverJar, destinationPath, true);
            }
            progress?.Report("Forge セットアップが完了しました。");
            return;
        }

        // Forge 1.17+ 形式: run.bat / run.sh が存在するか確認
        var runBat = Path.Combine(serverDir, "run.bat");
        var runSh = Path.Combine(serverDir, "run.sh");
        var userJvmArgs = Path.Combine(serverDir, "user_jvm_args.txt");

        if (File.Exists(runBat) || File.Exists(runSh))
        {
            // 1.17+形式の場合、server.jar は不要（run.bat/run.sh で起動する）
            // ダミーの server.jar を作成してアプリの起動ロジックと互換性を保つ
            if (!File.Exists(destinationPath))
            {
                // user_jvm_args.txt がなければ作成
                if (!File.Exists(userJvmArgs))
                {
                    await File.WriteAllTextAsync(userJvmArgs, "# Xmx and Xms are set by the launcher\n").ConfigureAwait(false);
                }

                // run.bat の内容からメインjarを読み取ってコピー
                var forgeJarInLibs = FindForgeLibraryJar(serverDir, versionId, forgeVersion);
                if (!string.IsNullOrWhiteSpace(forgeJarInLibs))
                {
                    File.Copy(forgeJarInLibs, destinationPath, true);
                }
                else
                {
                    // フォールバック: 空のマーカーファイル（起動は run.bat 経由）
                    await File.WriteAllTextAsync(destinationPath, "").ConfigureAwait(false);
                }
            }
            progress?.Report("Forge セットアップが完了しました（1.17+ 形式）。");
            return;
        }

        throw new InvalidOperationException(
            "Forge のセットアップ後にサーバーファイルが見つかりません。\n" +
            "インストーラーの実行に問題があった可能性があります。\n" +
            $"サーバーディレクトリ: {serverDir}");
    }

    private async Task DownloadFabricAsync(string versionId, string destinationPath, IProgress<string>? progress)
    {
        progress?.Report("Fabric Loader 情報を取得しています...");
        var loaderVersion = await GetLatestFabricLoaderVersionAsync(versionId).ConfigureAwait(false);
        var installerVersion = await GetLatestFabricInstallerVersionAsync().ConfigureAwait(false);

        var downloadUrl = $"{FabricApiBase}/loader/{versionId}/{loaderVersion}/{installerVersion}/server/jar";
        progress?.Report("Fabric サーバーをダウンロードしています...");
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

        // shim jar（1.17+の一部バージョン）
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
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        using var response = await _httpClient.SendAsync(request).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        await using var output = File.Create(destinationPath);
        await response.Content.CopyToAsync(output).ConfigureAwait(false);
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
            throw new InvalidOperationException("プロセスの起動に失敗しました。");
        }

        var stderrBuilder = new System.Text.StringBuilder();

        // progress が渡された場合は行単位でリアルタイム報告
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
                throw new InvalidOperationException($"プロセスがエラーコード {process.ExitCode} で終了しました:\n{detail}");
            }
        }
        else
        {
            // progress なし: 一括読み取り
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
                throw new InvalidOperationException($"プロセスがエラーコード {process.ExitCode} で終了しました:\n{detail}");
            }
        }
    }
}
