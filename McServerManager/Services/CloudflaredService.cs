using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;
using McServerManager.Utilities;

namespace McServerManager.Services;

public sealed class CloudflaredService : ICloudflaredService
{
    private const string CloudflaredLatestReleaseApi = "https://api.github.com/repos/cloudflare/cloudflared/releases/latest";
    private const string WindowsAssetName = "cloudflared-windows-amd64.exe";
    private const string TrustedSignerSubject = "CN=Cloudflare, Inc.";

    private static readonly string ExePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "MaiPilot", "cloudflared.exe");

    private static readonly Regex TunnelUrlRegex = new(
        @"https://[a-z0-9\-]+\.trycloudflare\.com",
        RegexOptions.Compiled);
    private static readonly Regex Sha256Regex = new(
        @"\b(?<hash>[A-Fa-f0-9]{64})\b",
        RegexOptions.Compiled);

    private Process? _process;

    // 複数インスタンス(サーバーごとの ResourcePack VM)が同時に同じ実行ファイルへ
    // ダウンロードして競合するのを防ぐため、インストールはプロセス全体で直列化する。
    private static readonly SemaphoreSlim InstallGate = new(1, 1);

    public bool IsRunning => _process is { HasExited: false };
    public string? TunnelUrl { get; private set; }

    public async Task EnsureInstalledAsync(IProgress<string> progress)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(ExePath)!);

        await InstallGate.WaitAsync().ConfigureAwait(false);
        try
        {
            progress.Report("HTTPS トンネル用 cloudflared のリリース情報を確認中...");

            using var client = CreateHttpClient();
            var latestAsset = await GetLatestWindowsAssetAsync(client).ConfigureAwait(false);

            // ゲート取得後に再確認(先行スレッドが導入済みなら即返却)。
            if (File.Exists(ExePath) && await IsExistingBinaryTrustedAsync(ExePath, latestAsset.Sha256).ConfigureAwait(false))
            {
                return;
            }

            progress.Report($"cloudflared {latestAsset.Version} を検証付きでダウンロード中...");
            // 一時ファイルは一意にして万一の競合を避ける。
            var tmp = $"{ExePath}.{Guid.NewGuid():N}.tmp";
            try
            {
                using var response = await client.GetAsync(latestAsset.DownloadUrl, HttpCompletionOption.ResponseHeadersRead).ConfigureAwait(false);
                response.EnsureSuccessStatusCode();

                await using (var fs = File.Create(tmp))
                    await response.Content.CopyToAsync(fs).ConfigureAwait(false);

                await ValidateDownloadedBinaryAsync(tmp, latestAsset.Sha256).ConfigureAwait(false);
                File.Move(tmp, ExePath, overwrite: true);
            }
            finally
            {
                if (File.Exists(tmp))
                {
                    try { File.Delete(tmp); } catch { }
                }
            }
        }
        finally
        {
            InstallGate.Release();
        }
    }

    private sealed record CloudflaredAsset(string DownloadUrl, string Sha256, string Version);

    private static HttpClient CreateHttpClient()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromMinutes(3) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("MaiPilot/1.0");
        client.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
        return client;
    }

    private static async Task<CloudflaredAsset> GetLatestWindowsAssetAsync(HttpClient client)
    {
        using var response = await client.GetAsync(CloudflaredLatestReleaseApi, HttpCompletionOption.ResponseContentRead).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        var json = await response.Content.ReadAsStringAsync().ConfigureAwait(false);

        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        var version = root.TryGetProperty("tag_name", out var tagElement)
            ? tagElement.GetString() ?? "latest"
            : "latest";

        if (!root.TryGetProperty("assets", out var assetsElement) || assetsElement.ValueKind != JsonValueKind.Array)
        {
            throw new InvalidOperationException("cloudflared リリース情報が不正です (assets 不在)。");
        }

        foreach (var asset in assetsElement.EnumerateArray())
        {
            var assetName = asset.TryGetProperty("name", out var nameElement)
                ? nameElement.GetString() ?? string.Empty
                : string.Empty;
            if (!string.Equals(assetName, WindowsAssetName, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var downloadUrl = asset.TryGetProperty("browser_download_url", out var urlElement)
                ? urlElement.GetString() ?? string.Empty
                : string.Empty;
            if (string.IsNullOrWhiteSpace(downloadUrl))
            {
                throw new InvalidOperationException("cloudflared リリース情報が不正です (download URL 不在)。");
            }

            var digest = asset.TryGetProperty("digest", out var digestElement)
                ? digestElement.GetString() ?? string.Empty
                : string.Empty;
            var sha256 = ExtractSha256(digest);
            if (string.IsNullOrWhiteSpace(sha256))
            {
                var body = root.TryGetProperty("body", out var bodyElement)
                    ? bodyElement.GetString() ?? string.Empty
                    : string.Empty;
                sha256 = ExtractSha256FromReleaseBody(body, WindowsAssetName);
            }

            if (string.IsNullOrWhiteSpace(sha256))
            {
                throw new InvalidOperationException("cloudflared SHA256 をリリース情報から取得できませんでした。");
            }

            return new CloudflaredAsset(downloadUrl, sha256, version);
        }

        throw new InvalidOperationException($"cloudflared の {WindowsAssetName} が最新リリースに見つかりません。");
    }

    private static string ExtractSha256(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return string.Empty;
        }

        var match = Sha256Regex.Match(text);
        return match.Success ? match.Groups["hash"].Value.ToLowerInvariant() : string.Empty;
    }

    private static string ExtractSha256FromReleaseBody(string body, string assetName)
    {
        if (string.IsNullOrWhiteSpace(body) || string.IsNullOrWhiteSpace(assetName))
        {
            return string.Empty;
        }

        foreach (var line in body.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries))
        {
            if (line.IndexOf(assetName, StringComparison.OrdinalIgnoreCase) < 0)
            {
                continue;
            }

            var sha256 = ExtractSha256(line);
            if (!string.IsNullOrWhiteSpace(sha256))
            {
                return sha256;
            }
        }

        return string.Empty;
    }

    private static async Task<bool> IsExistingBinaryTrustedAsync(string filePath, string expectedSha256)
    {
        try
        {
            await ValidateDownloadedBinaryAsync(filePath, expectedSha256).ConfigureAwait(false);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static async Task ValidateDownloadedBinaryAsync(string filePath, string expectedSha256)
    {
        var actualSha256 = await ComputeSha256Async(filePath).ConfigureAwait(false);
        if (!string.Equals(actualSha256, expectedSha256, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"cloudflared SHA256 が一致しません。expected={expectedSha256}, actual={actualSha256}");
        }

        if (!AuthenticodeVerifier.TryVerify(filePath, out var signerSubject, out var signError))
        {
            throw new InvalidOperationException($"cloudflared の Authenticode 検証に失敗しました: {signError}");
        }

        if (!IsTrustedSignerSubject(signerSubject))
        {
            throw new InvalidOperationException(
                $"cloudflared の署名 Subject が想定外です。expected={TrustedSignerSubject}, actual={signerSubject ?? "(null)"}");
        }
    }

    private static async Task<string> ComputeSha256Async(string path)
    {
        await using var stream = File.OpenRead(path);
        using var sha = SHA256.Create();
        var hash = await sha.ComputeHashAsync(stream).ConfigureAwait(false);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    private static bool IsTrustedSignerSubject(string? signerSubject)
    {
        if (string.IsNullOrWhiteSpace(signerSubject))
        {
            return false;
        }

        // X509Certificate2.Subject はカンマを含む値を引用符で囲む
        // (例: CN="Cloudflare, Inc.", O="Cloudflare, Inc.", ...)。
        // 引用符と空白を除去して正規化し、CN 部分を照合する。
        static string Normalize(string value) =>
            value.Replace("\"", string.Empty).Replace(" ", string.Empty);

        var normalizedSubject = Normalize(signerSubject);
        var normalizedExpected = Normalize(TrustedSignerSubject);

        return normalizedSubject.Contains(normalizedExpected, StringComparison.OrdinalIgnoreCase);
    }

    public async Task<string> StartTunnelAsync(int localPort, IProgress<string> progress)
    {
        if (IsRunning)
            await StopAsync();

        if (!File.Exists(ExePath))
        {
            throw new InvalidOperationException(
                "cloudflared がインストールされていません。ネットワーク接続を確認し、しばらく待ってから再度お試しください。");
        }

        TunnelUrl = null;
        var tcs = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);

        // cloudflared の最近の出力を保持し、失敗時に原因として提示する。
        var recentOutput = new System.Collections.Generic.Queue<string>();
        var outputLock = new object();
        void Capture(string line)
        {
            lock (outputLock)
            {
                recentOutput.Enqueue(line);
                while (recentOutput.Count > 25) recentOutput.Dequeue();
            }
        }
        string OutputTail()
        {
            lock (outputLock)
            {
                return recentOutput.Count == 0 ? "(出力なし)" : string.Join("\n", recentOutput);
            }
        }

        _process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = ExePath,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            },
            EnableRaisingEvents = true
        };
        _process.StartInfo.ArgumentList.Add("tunnel");
        _process.StartInfo.ArgumentList.Add("--url");
        _process.StartInfo.ArgumentList.Add($"http://localhost:{localPort}");

        void OnData(object _, DataReceivedEventArgs e)
        {
            if (e.Data is null) return;
            Capture(e.Data);
            if (tcs.Task.IsCompleted) return;
            var m = TunnelUrlRegex.Match(e.Data);
            if (m.Success) tcs.TrySetResult(m.Value);
        }

        _process.OutputDataReceived += OnData;
        _process.ErrorDataReceived += OnData;
        _process.Exited += (_, _) =>
        {
            if (!tcs.Task.IsCompleted)
                tcs.TrySetException(new Exception(
                    $"cloudflared が URL を出力せずに終了しました。\n\ncloudflared の出力:\n{OutputTail()}"));
        };

        _process.Start();
        _process.BeginOutputReadLine();
        _process.BeginErrorReadLine();

        progress.Report("HTTPS トンネルを確立中（最大60秒）...");

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        await using var reg = cts.Token.Register(() => tcs.TrySetCanceled());

        TunnelUrl = await tcs.Task;
        return TunnelUrl;
    }

    public Task StopAsync()
    {
        if (_process is { HasExited: false })
            try { _process.Kill(entireProcessTree: true); } catch { }
        _process?.Dispose();
        _process = null;
        TunnelUrl = null;
        return Task.CompletedTask;
    }

    public void Dispose()
    {
        if (_process is { HasExited: false })
            try { _process.Kill(entireProcessTree: true); } catch { }
        _process?.Dispose();
    }
}
