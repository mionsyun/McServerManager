using System.IO;
using System.Net;
using System.Security.Cryptography;

namespace McServerManager.Services;

public sealed class ResourcePackService : IResourcePackService
{
    private HttpListener? _listener;
    private CancellationTokenSource? _cts;
    private string? _filePath;
    private Task? _loopTask;

    public bool IsRunning => _listener?.IsListening ?? false;

    public async Task StartAsync(string filePath, int port)
    {
        if (IsRunning)
            await StopAsync();

        _filePath = filePath;
        _listener = new HttpListener();
        _listener.Prefixes.Add($"http://+:{port}/");
        _listener.Start();

        _cts = new CancellationTokenSource();
        _loopTask = ServeLoopAsync(_cts.Token);
    }

    public async Task StopAsync()
    {
        _cts?.Cancel();
        try { _listener?.Stop(); } catch { }
        if (_loopTask != null)
        {
            try { await _loopTask; } catch { }
            _loopTask = null;
        }
        _listener?.Close();
        _listener = null;
        _cts = null;
    }

    public string ComputeSha1(string filePath)
    {
        using var stream = File.OpenRead(filePath);
        var hash = SHA1.HashData(stream);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    private async Task ServeLoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested && (_listener?.IsListening == true))
        {
            HttpListenerContext context;
            try
            {
                context = await _listener!.GetContextAsync().WaitAsync(ct);
            }
            catch (OperationCanceledException) { break; }
            catch { break; }

            _ = HandleAsync(context);
        }
    }

    private async Task HandleAsync(HttpListenerContext context)
    {
        try
        {
            if (_filePath == null || !File.Exists(_filePath))
            {
                context.Response.StatusCode = 404;
                return;
            }

            context.Response.ContentType = "application/octet-stream";
            context.Response.AddHeader(
                "Content-Disposition",
                $"attachment; filename=\"{Path.GetFileName(_filePath)}\"");

            await using var fs = File.OpenRead(_filePath);
            context.Response.ContentLength64 = fs.Length;
            await fs.CopyToAsync(context.Response.OutputStream);
        }
        catch
        {
            // Best-effort response
        }
        finally
        {
            try { context.Response.Close(); } catch { }
        }
    }
}
