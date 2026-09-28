using System.IO.Compression;
using System.Net.Http;
using System.Text.Json;
using System.Text.RegularExpressions;
using McServerManager.Models;

namespace McServerManager.Services;

public sealed class BedrockServerService : IBedrockServerService
{
    private const string ReleaseDownloadType = "serverBedrockWindows";
    private const string PreviewDownloadType = "serverBedrockPreviewWindows";
    private static readonly TimeSpan ApiTimeout = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan DownloadTimeout = TimeSpan.FromMinutes(15);
    private static readonly Regex VersionFromFileNameRegex = new(
        @"bedrock-server-(?<version>\d+(?:\.\d+){2,3})\.zip",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    /// <summary>更新時に上書きしないユーザー設定ファイル（BDS 公式の更新手順で保持が推奨されているもの）。</summary>
    private static readonly HashSet<string> PreservedFiles = new(StringComparer.OrdinalIgnoreCase)
    {
        "server.properties",
        "allowlist.json",
        "permissions.json",
        "whitelist.json"
    };

    private readonly HttpClient _httpClient;

    public BedrockServerService(HttpClient? httpClient = null)
    {
        _httpClient = httpClient ?? new HttpClient { Timeout = DownloadTimeout };
        // minecraft.net の配布 CDN はブラウザ以外の User-Agent を拒否することがある
        _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd(
            "Mozilla/5.0 (Windows NT 10.0; Win64; x64) MaiPilot/2.0");
        _httpClient.DefaultRequestHeaders.Accept.ParseAdd("*/*");
    }

    public async Task<IReadOnlyList<BedrockVersionInfo>> GetAvailableVersionsAsync(CancellationToken cancellationToken = default)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(ApiTimeout);

        using var response = await _httpClient
            .GetAsync(ExternalApiUrls.BedrockDownloadLinks, timeout.Token)
            .ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        var json = await response.Content.ReadAsStringAsync(timeout.Token).ConfigureAwait(false);

        var versions = ParseDownloadLinks(json);
        if (versions.Count == 0)
        {
            throw new InvalidOperationException("統合版サーバーの配布情報が見つかりませんでした。");
        }

        return versions;
    }

    public async Task InstallFromUrlAsync(
        string downloadUrl,
        string serverDirectory,
        IProgress<string>? progress = null,
        CancellationToken cancellationToken = default)
    {
        if (!IsTrustedDownloadUrl(downloadUrl))
        {
            throw new InvalidOperationException($"公式以外のダウンロード URL は使用できません: {downloadUrl}");
        }

        var tempZip = Path.Combine(Path.GetTempPath(), $"MaiPilot-bds-{Guid.NewGuid():N}.zip");
        try
        {
            await DownloadAsync(downloadUrl, tempZip, progress, cancellationToken).ConfigureAwait(false);
            await InstallFromZipAsync(tempZip, serverDirectory, progress, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            TryDeleteFile(tempZip);
        }
    }

    public Task InstallFromZipAsync(
        string zipPath,
        string serverDirectory,
        IProgress<string>? progress = null,
        CancellationToken cancellationToken = default)
    {
        if (!File.Exists(zipPath))
        {
            throw new FileNotFoundException("統合版サーバーの zip が見つかりません。", zipPath);
        }

        progress?.Report("統合版サーバーを展開中...");
        return Task.Run(() => ExtractServerZip(zipPath, serverDirectory, cancellationToken), cancellationToken);
    }

    public bool IsInstalled(string serverDirectory) =>
        !string.IsNullOrWhiteSpace(serverDirectory)
        && File.Exists(Path.Combine(serverDirectory, ServerEditions.BedrockExecutableName));

    /// <summary>ダウンロードリンク API の JSON から Windows 版 BDS を抜き出す。正式版 → プレビューの順。</summary>
    public static IReadOnlyList<BedrockVersionInfo> ParseDownloadLinks(string json)
    {
        using var document = JsonDocument.Parse(json);
        var found = new List<BedrockVersionInfo>();
        CollectDownloadLinks(document.RootElement, found);

        return found
            .GroupBy(v => v.IsPreview)
            .Select(g => g.First())
            .OrderBy(v => v.IsPreview)
            .ToList();
    }

    /// <summary>"bedrock-server-1.21.100.7.zip" のようなファイル名・URL からバージョンを取り出す。</summary>
    public static string? TryGetVersionFromFileName(string? pathOrUrl)
    {
        if (string.IsNullOrWhiteSpace(pathOrUrl))
        {
            return null;
        }

        var match = VersionFromFileNameRegex.Match(pathOrUrl);
        return match.Success ? match.Groups["version"].Value : null;
    }

    public static bool IsTrustedDownloadUrl(string? url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps)
        {
            return false;
        }

        return uri.Host.Equals("minecraft.net", StringComparison.OrdinalIgnoreCase)
               || uri.Host.EndsWith(".minecraft.net", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// BDS の zip をサーバーフォルダへ展開する。
    /// server.properties / allowlist.json / permissions.json が既にあれば上書きしない（更新時に設定を保持）。
    /// worlds フォルダは zip に含まれないため影響しない。
    /// </summary>
    public static void ExtractServerZip(string zipPath, string serverDirectory, CancellationToken cancellationToken = default)
    {
        using var archive = ZipFile.OpenRead(zipPath);
        if (!archive.Entries.Any(e => string.Equals(
                NormalizeEntryPath(e.FullName), ServerEditions.BedrockExecutableName, StringComparison.OrdinalIgnoreCase)))
        {
            throw new InvalidOperationException(
                "Windows 版の統合版サーバー (bedrock_server.exe) が含まれていない zip です。公式サイトの「Windows」版をダウンロードしてください。");
        }

        Directory.CreateDirectory(serverDirectory);
        var root = Path.GetFullPath(serverDirectory);
        var rootWithSeparator = root.EndsWith(Path.DirectorySeparatorChar) ? root : root + Path.DirectorySeparatorChar;

        foreach (var entry in archive.Entries)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var relativePath = NormalizeEntryPath(entry.FullName);
            if (string.IsNullOrEmpty(relativePath))
            {
                continue;
            }

            var destination = Path.GetFullPath(Path.Combine(root, relativePath));
            if (!destination.StartsWith(rootWithSeparator, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException($"不正なパスを含む zip です: {entry.FullName}");
            }

            if (entry.FullName.EndsWith('/') || entry.FullName.EndsWith('\\'))
            {
                Directory.CreateDirectory(destination);
                continue;
            }

            if (PreservedFiles.Contains(relativePath) && File.Exists(destination))
            {
                continue;
            }

            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            entry.ExtractToFile(destination, overwrite: true);
        }
    }

    private async Task DownloadAsync(string url, string destinationPath, IProgress<string>? progress, CancellationToken cancellationToken)
    {
        progress?.Report("統合版サーバーをダウンロード中...");
        using var response = await _httpClient
            .GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
            .ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        var totalBytes = response.Content.Headers.ContentLength;
        await using var source = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        await using var target = File.Create(destinationPath);

        var buffer = new byte[81920];
        long received = 0;
        var lastReportedPercent = -1;
        int read;
        while ((read = await source.ReadAsync(buffer, cancellationToken).ConfigureAwait(false)) > 0)
        {
            await target.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
            received += read;

            if (totalBytes is > 0)
            {
                var percent = (int)(received * 100 / totalBytes.Value);
                if (percent != lastReportedPercent && percent % 5 == 0)
                {
                    lastReportedPercent = percent;
                    progress?.Report($"統合版サーバーをダウンロード中... {percent}% ({received / 1048576.0:F0} / {totalBytes.Value / 1048576.0:F0} MB)");
                }
            }
        }
    }

    private static void CollectDownloadLinks(JsonElement element, List<BedrockVersionInfo> found)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                if (TryReadLink(element, out var info))
                {
                    found.Add(info);
                }

                foreach (var property in element.EnumerateObject())
                {
                    CollectDownloadLinks(property.Value, found);
                }
                break;
            case JsonValueKind.Array:
                foreach (var item in element.EnumerateArray())
                {
                    CollectDownloadLinks(item, found);
                }
                break;
        }
    }

    private static bool TryReadLink(JsonElement element, out BedrockVersionInfo info)
    {
        info = new BedrockVersionInfo();
        if (!element.TryGetProperty("downloadType", out var typeElement)
            || !element.TryGetProperty("downloadUrl", out var urlElement)
            || typeElement.ValueKind != JsonValueKind.String
            || urlElement.ValueKind != JsonValueKind.String)
        {
            return false;
        }

        var type = typeElement.GetString();
        var url = urlElement.GetString();
        var isRelease = string.Equals(type, ReleaseDownloadType, StringComparison.OrdinalIgnoreCase);
        var isPreview = string.Equals(type, PreviewDownloadType, StringComparison.OrdinalIgnoreCase);
        if ((!isRelease && !isPreview) || !IsTrustedDownloadUrl(url))
        {
            return false;
        }

        info = new BedrockVersionInfo
        {
            Version = TryGetVersionFromFileName(url) ?? "latest",
            DownloadUrl = url!,
            IsPreview = isPreview
        };
        return true;
    }

    private static string NormalizeEntryPath(string entryPath) =>
        entryPath.Replace('\\', '/').Trim('/').Replace('/', Path.DirectorySeparatorChar);

    private static void TryDeleteFile(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch
        {
            // 一時ファイルの削除失敗は無視（OS の一時フォルダ掃除に任せる）
        }
    }
}
