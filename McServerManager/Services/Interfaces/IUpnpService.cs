namespace McServerManager.Services;

public interface IUpnpService
{
    Task<(bool ok, string? error)> TryOpenPortAsync(int port, string description);
    Task TryClosePortAsync(int port);
}
