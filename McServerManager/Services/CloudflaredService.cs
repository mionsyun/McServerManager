using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Text.RegularExpressions;

namespace McServerManager.Services;

public sealed class CloudflaredService : ICloudflaredService
{
    private static readonly string ExePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "MaiPilot", "cloudflared.exe");

    private static readonly Regex TunnelUrlRegex = new(
        @"https://[a-z0-9\-]+\.trycloudflare\.com",
        RegexOptions.Compiled);

    private Process? _process;

    public bool IsRunning => _process is { HasExited: false };
    public string? TunnelUrl { get; private set; }

    public async Task EnsureInstalledAsync(IProgress<string> progress)
    {
        if (File.Exists(ExePath))
            return;

        Directory.CreateDirectory(Path.GetDirectoryName(ExePath)!);
        progress.Report("HTTPS トンネル用コンポーネントをダウンロード中...");

        using var client = new HttpClient { Timeout = TimeSpan.FromMinutes(3) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("MaiPilot/1.0");

        const string url = "https://github.com/cloudflare/cloudflared/releases/latest/download/cloudflared-windows-amd64.exe";
        using var response = await client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead);
        response.EnsureSuccessStatusCode();

        var tmp = ExePath + ".tmp";
        await using (var fs = File.Create(tmp))
            await response.Content.CopyToAsync(fs);

        File.Move(tmp, ExePath, overwrite: true);
    }

    public async Task<string> StartTunnelAsync(int localPort, IProgress<string> progress)
    {
        if (IsRunning)
            await StopAsync();

        TunnelUrl = null;
        var tcs = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);

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
            if (e.Data is null || tcs.Task.IsCompleted) return;
            var m = TunnelUrlRegex.Match(e.Data);
            if (m.Success) tcs.TrySetResult(m.Value);
        }

        _process.OutputDataReceived += OnData;
        _process.ErrorDataReceived += OnData;
        _process.Exited += (_, _) =>
        {
            if (!tcs.Task.IsCompleted)
                tcs.TrySetException(new Exception("cloudflared が予期せず終了しました"));
        };

        _process.Start();
        _process.BeginOutputReadLine();
        _process.BeginErrorReadLine();

        progress.Report("HTTPS トンネルを確立中（最大40秒）...");

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(40));
        cts.Token.Register(() => tcs.TrySetCanceled());

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
