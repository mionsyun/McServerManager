using System.Threading;

namespace McServerManager.Services.RuntimeLifecycle;

/// <summary>A process-local exclusion lease only; not cross-instance, directory or process-tree ownership.</summary>
public interface IRuntimeOperationCoordinator
{
    ValueTask<IDisposable> EnterAsync(CancellationToken cancellationToken = default);
    bool TryEnter(out IDisposable? lease);
}
