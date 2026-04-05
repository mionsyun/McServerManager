namespace McServerManager.Services;

public interface ICloudflaredService : IDisposable
{
    bool IsRunning { get; }
    string? TunnelUrl { get; }
    Task EnsureInstalledAsync(IProgress<string> progress);
    Task<string> StartTunnelAsync(int localPort, IProgress<string> progress);
    Task StopAsync();
}
