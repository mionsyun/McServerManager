using System.Threading;

namespace McServerManager.Services.RuntimeLifecycle;

/// <summary>
/// Separate from the process-local operation gate. A production capture must obtain native
/// stopped-tree, directory-identity and cross-instance exclusion evidence through this boundary.
/// </summary>
public interface IStoppedRuntimeCaptureLeaseProvider
{
    ValueTask<IDisposable> AcquireAsync(
        string serverId, string serverDirectory, CancellationToken cancellationToken = default);
}
