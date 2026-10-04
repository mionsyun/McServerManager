using System.Threading;

namespace McServerManager.Services.RuntimeLifecycle;

/// <summary>
/// All production managers and future inspection/capture callers must use Shared.
/// The deliberately global gate also excludes differently named servers referring to the same directory.
/// A lease does not certify that a server is stopped and is not a cross-process lock.
/// </summary>
public sealed class RuntimeOperationCoordinator : IRuntimeOperationCoordinator
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    public static RuntimeOperationCoordinator Shared { get; } = new();

    public async ValueTask<IDisposable> EnterAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        return new Lease(_gate);
    }

    public bool TryEnter(out IDisposable? lease)
    {
        lease = _gate.Wait(0) ? new Lease(_gate) : null;
        return lease is not null;
    }

    private sealed class Lease(SemaphoreSlim gate) : IDisposable
    {
        private SemaphoreSlim? _gate = gate;
        public void Dispose() => Interlocked.Exchange(ref _gate, null)?.Release();
    }
}
