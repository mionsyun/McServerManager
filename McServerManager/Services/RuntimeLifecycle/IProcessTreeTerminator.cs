using System.Threading;
using McServerManager.Models.RuntimeLifecycle;

namespace McServerManager.Services.RuntimeLifecycle;

public interface IProcessTreeTerminator
{
    Task<ProcessStopResult> StopAsync(
        IServerProcess process, TimeSpan timeout, CancellationToken cancellationToken = default);
}
