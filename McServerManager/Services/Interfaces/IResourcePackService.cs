namespace McServerManager.Services;

public interface IResourcePackService
{
    bool IsRunning { get; }
    Task StartAsync(string filePath, int port);
    Task StopAsync();
    string ComputeSha1(string filePath);
}
