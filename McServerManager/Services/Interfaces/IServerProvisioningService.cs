using McServerManager.Models;

namespace McServerManager.Services;

public interface IServerProvisioningService
{
    Task<ServerConfig> CreateAsync(NewServerOptions options, IProgress<string>? progress = null);
}
