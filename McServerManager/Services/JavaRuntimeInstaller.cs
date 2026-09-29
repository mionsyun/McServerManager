using System.IO.Compression;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text.Json;

namespace McServerManager.Services;

public sealed class JavaRuntimeInstaller : IJavaRuntimeInstaller
{
    private static readonly int[] LtsMajors = [8, 11, 17, 21, 25];
    private const int DefaultMajor = 21;
    private static readonly TimeSpan ApiTimeout = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan DownloadTimeout = TimeSpan.FromMinutes(15);

    private readonly string _runtimesRoot;
    private readonly IJavaService _javaService;
    private readonly HttpClient _httpClient;

    public JavaRuntimeInstaller(AppPathsService paths, IJavaService javaService, HttpClient? httpClient = null)
    {
        _runtimesRoot = Path.Combine(paths.RootPath, "runtimes", "java");
        _javaService = javaService;
        _httpClient = httpClient ?? new HttpClient { Timeout = DownloadTimeout };
        _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("MaiPilot/2.1 (+https://www.maipilot.jp)");
    }

    public int GetInstallMajor(int? requiredMajor)
    {
        if (requiredMajor is null)
            return DefaultMajor;

        // 必要な版以上で最も近い LTS（例: 16 → 17）。それより新しい要件なら要件どおりの版
        var lts = LtsMajors.FirstOrDefault(v => v >= requiredMajor.Value);
        return lts != 0 ? lts : requiredMajor.Value;
    }

    public string? FindInstalled(int major)
    {
        var javaExe = Path.Combine(GetInstallDirectory(major), "bin", "java.exe");
        return File.Exists(javaExe) ? javaExe : null;
    }

    public async Task<string> InstallAsync(int major, IProgress<string>? progress = null, CancellationToken cancellationToken = default)
    {
        var installed = FindInstalled(major);
        if (installed is not null)
            return installed;

        progress?.Report($"Java {major} の最新版を確認しています...");
        var asset = await GetLatestAssetAsync(major, cancellationToken).ConfigureAwait(false);

        Directory.CreateDirectory(_runtimesRoot);
        var tempZip = Path.Combine(Path.GetTempPath(), $"MaiPilot-java-{Guid.NewGuid():N}.zip");
        var extractDir = Path.Combine(_runtimesRoot, $"_extract-{Guid.NewGuid():N}");
        try
        {
            await DownloadAsync(asset, tempZip, progress, cancellationToken).ConfigureAwait(false);

            progress?.Report("ダウンロードしたファイルを確認しています...");
            var actualHash = await ComputeSha256Async(tempZip, cancellationToken).ConfigureAwait(false);
            if (!string.Equals(actualHash, asset.Sha256, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(
                    $"ダウンロードした Java のチェックサムが一致しません。もう一度お試しください。expected={asset.Sha256}, actual={actualHash}");
            }

            progress?.Report("Java を展開しています...");
            await Task.Run(() => ExtractJavaHome(tempZip, extractDir, cancellationToken), cancellationToken).ConfigureAwait(false);
            var javaHome = FindJavaHome(extractDir)
                ?? throw new InvalidOperationException("展開した Java に java.exe が見つかりません。");

            var installDir = GetInstallDirectory(major);
            if (Directory.Exists(installDir))
                Directory.Delete(installDir, recursive: true);
            Directory.Move(javaHome, installDir);

            var javaExe = Path.Combine(installDir, "bin", "java.exe");
            if (!_javaService.TryGetJavaMajorVersion(javaExe, out var actualMajor, out _) || actualMajor != major)
            {
                throw new InvalidOperationException($"導入した Java {major} を起動できませんでした。");
            }

            progress?.Report($"Java {major} ({asset.ReleaseName}) の準備ができました。");
            return javaExe;
        }
        finally
        {
            TryDeleteFile(tempZip);
            TryDeleteDirectory(extractDir);
        }
    }

    /// <summary>Adoptium API の assets/latest 応答から、Windows x64 の JRE ZIP を取り出す。</summary>
    public static JavaRuntimeAsset ParseLatestAsset(string json)
    {
        using var document = JsonDocument.Parse(json);
        if (document.RootElement.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in document.RootElement.EnumerateArray())
            {
                if (!item.TryGetProperty("binary", out var binary)
                    || !binary.TryGetProperty("package", out var package)
                    || !package.TryGetProperty("link", out var link)
                    || !package.TryGetProperty("checksum", out var checksum)
                    || link.GetString() is not { } url
                    || !url.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var size = package.TryGetProperty("size", out var sizeElement) && sizeElement.TryGetInt64(out var s) ? s : 0;
                var releaseName = item.TryGetProperty("release_name", out var release) ? release.GetString() ?? string.Empty : string.Empty;
                return new JavaRuntimeAsset(url, checksum.GetString() ?? string.Empty, size, releaseName);
            }
        }

        throw new InvalidOperationException("Windows 用の Java (ZIP) が配布情報に見つかりませんでした。");
    }

    /// <summary>Adoptium のバイナリは GitHub のリリースで配布される。それ以外からは取得しない。</summary>
    public static bool IsTrustedDownloadUrl(string? url) =>
        Uri.TryCreate(url, UriKind.Absolute, out var uri)
        && uri.Scheme == Uri.UriSchemeHttps
        && uri.Host.Equals("github.com", StringComparison.OrdinalIgnoreCase)
        && uri.AbsolutePath.StartsWith("/adoptium/", StringComparison.OrdinalIgnoreCase);

    /// <summary>ZIP を展開する。展開先の外へ出るパスを含む ZIP は拒否する。</summary>
    public static void ExtractJavaHome(string zipPath, string destination, CancellationToken cancellationToken = default)
    {
        var root = Path.GetFullPath(destination);
        var rootWithSeparator = root.EndsWith(Path.DirectorySeparatorChar) ? root : root + Path.DirectorySeparatorChar;
        Directory.CreateDirectory(root);

        using var archive = ZipFile.OpenRead(zipPath);
        foreach (var entry in archive.Entries)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var target = Path.GetFullPath(Path.Combine(root, entry.FullName));
            if (!target.StartsWith(rootWithSeparator, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException($"不正なパスを含む ZIP です: {entry.FullName}");

            if (entry.FullName.EndsWith('/') || entry.FullName.EndsWith('\\'))
            {
                Directory.CreateDirectory(target);
                continue;
            }

            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            entry.ExtractToFile(target, overwrite: true);
        }
    }

    /// <summary>展開先から bin\java.exe を持つフォルダ（JAVA_HOME）を探す。ZIP は通常 1 階層下にある。</summary>
    public static string? FindJavaHome(string extractedRoot)
    {
        if (File.Exists(Path.Combine(extractedRoot, "bin", "java.exe")))
            return extractedRoot;

        return Directory.EnumerateDirectories(extractedRoot)
            .FirstOrDefault(dir => File.Exists(Path.Combine(dir, "bin", "java.exe")));
    }

    private string GetInstallDirectory(int major) => Path.Combine(_runtimesRoot, $"temurin-{major}-jre");

    private async Task<JavaRuntimeAsset> GetLatestAssetAsync(int major, CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(ApiTimeout);
        var url = string.Format(ExternalApiUrls.AdoptiumLatestJreTemplate, major);
        using var response = await _httpClient.GetAsync(url, timeout.Token).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        var json = await response.Content.ReadAsStringAsync(timeout.Token).ConfigureAwait(false);

        var asset = ParseLatestAsset(json);
        if (!IsTrustedDownloadUrl(asset.DownloadUrl))
            throw new InvalidOperationException($"想定外の配布元のため中止しました: {asset.DownloadUrl}");
        if (string.IsNullOrWhiteSpace(asset.Sha256))
            throw new InvalidOperationException("配布情報にチェックサムがないため中止しました。");
        return asset;
    }

    private async Task DownloadAsync(JavaRuntimeAsset asset, string destination, IProgress<string>? progress, CancellationToken cancellationToken)
    {
        using var response = await _httpClient
            .GetAsync(asset.DownloadUrl, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
            .ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        var total = response.Content.Headers.ContentLength ?? asset.Size;
        await using var source = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        await using var target = File.Create(destination);

        var buffer = new byte[81920];
        long received = 0;
        var lastPercent = -1;
        int read;
        while ((read = await source.ReadAsync(buffer, cancellationToken).ConfigureAwait(false)) > 0)
        {
            await target.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
            received += read;
            if (total > 0)
            {
                var percent = (int)(received * 100 / total);
                if (percent != lastPercent && percent % 5 == 0)
                {
                    lastPercent = percent;
                    progress?.Report($"Java をダウンロード中... {percent}% ({received / 1048576.0:F0} / {total / 1048576.0:F0} MB)");
                }
            }
        }
    }

    private static async Task<string> ComputeSha256Async(string path, CancellationToken cancellationToken)
    {
        await using var stream = File.OpenRead(path);
        var hash = await SHA256.HashDataAsync(stream, cancellationToken).ConfigureAwait(false);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    private static void TryDeleteFile(string path)
    {
        try
        {
            if (File.Exists(path))
                File.Delete(path);
        }
        catch (IOException)
        {
            // 一時ファイルは OS の掃除に任せる
        }
    }

    private static void TryDeleteDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path))
                Directory.Delete(path, recursive: true);
        }
        catch (IOException)
        {
            // 作業フォルダの削除失敗は次回以降に影響しない
        }
    }
}

/// <summary>Adoptium の配布情報（Windows x64 JRE ZIP）。</summary>
public sealed record JavaRuntimeAsset(string DownloadUrl, string Sha256, long Size, string ReleaseName);
