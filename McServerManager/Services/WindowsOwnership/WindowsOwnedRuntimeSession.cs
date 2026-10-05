using System.Diagnostics;
using McServerManager.Services.RuntimeLifecycle;

namespace McServerManager.Services.WindowsOwnership;

/// <summary>
/// Experimental foundation for newly created, cooperating local runtimes only.
/// Not registered with ServerRuntimeManager or IStoppedRuntimeCaptureLeaseProvider.
/// A stopped lease is not a filesystem snapshot or authority to capture/apply.
/// </summary>
public sealed class WindowsOwnedRuntimeSession : IAsyncDisposable
{
    private enum State { Fresh, Running, Stopped, Unknown, Disposed }
    private readonly WindowsOwnedDirectory _directory;
    private readonly IWindowsOwnedJob _job;
    private readonly IRuntimeOperationCoordinator _operations;
    private State _state;
    public string DirectoryPath => _directory.Path;

    private WindowsOwnedRuntimeSession(WindowsOwnedDirectory directory, IWindowsOwnedJob job, IRuntimeOperationCoordinator operations)
    { _directory = directory; _job = job; _operations = operations; }

    public static WindowsOwnedRuntimeSession CreateNew(string directory) => CreateNew(directory, RuntimeOperationCoordinator.Shared, () => new WindowsOwnedJob());

    internal static WindowsOwnedRuntimeSession CreateNew(string directory, IRuntimeOperationCoordinator operations, Func<IWindowsOwnedJob> jobFactory)
    {
        using var operation = operations.TryEnter(out var lease) ? lease : throw new InvalidOperationException("Runtime operation is busy.");
        var pinned = WindowsOwnedDirectory.CreateNew(directory);
        try { return new WindowsOwnedRuntimeSession(pinned, jobFactory(), operations); }
        catch { pinned.Dispose(); throw; }
    }

    /// <summary>Trusted executable only. This does not implement a template-provided executable or command.</summary>
    public async Task StartAsync(string executable, IReadOnlyList<string> arguments, CancellationToken cancellationToken = default)
    {
        using var operation = await _operations.EnterAsync(cancellationToken).ConfigureAwait(false);
        ThrowIfDisposed();
        if (_state is not (State.Fresh or State.Stopped)) throw new InvalidOperationException("Ownership must be confirmed stopped before start.");
        _directory.Validate();
        cancellationToken.ThrowIfCancellationRequested();
        _state = State.Unknown;
        try { _job.Start(executable, arguments, _directory.Path, cancellationToken); _state = State.Running; }
        catch
        {
            // A failed/cancelled create may already own a suspended process. Keep all reservations until explicit Stop.
            try { _job.Terminate(); } catch { }
            throw;
        }
    }

    public async Task StopAsync(TimeSpan timeout, CancellationToken cancellationToken = default)
    {
        if (timeout <= TimeSpan.Zero || timeout > TimeSpan.FromMinutes(1)) throw new ArgumentOutOfRangeException(nameof(timeout));
        using var operation = await _operations.EnterAsync(cancellationToken).ConfigureAwait(false);
        ThrowIfDisposed();
        cancellationToken.ThrowIfCancellationRequested();
        _state = State.Unknown;
        await StopCoreAsync(timeout, cancellationToken).ConfigureAwait(false);
        _state = State.Stopped;
    }

    private async Task StopCoreAsync(TimeSpan timeout, CancellationToken cancellationToken)
    {
        _job.Terminate();
        var timer = Stopwatch.StartNew();
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (_job.IsEmpty) { _directory.Validate(); return; }
            if (timer.Elapsed >= timeout) throw new TimeoutException("Owned process tree has not been confirmed empty.");
            await Task.Delay(20, cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>Excludes starts and disposal until released. Does not adopt externally stopped/unknown sessions.</summary>
    public async ValueTask<IDisposable> AcquireStoppedLeaseAsync(CancellationToken cancellationToken = default)
    {
        var operation = await _operations.EnterAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ThrowIfDisposed();
            if (_state != State.Stopped) throw new InvalidOperationException("An explicit successful stop is required.");
            _state = State.Unknown;
            _directory.Validate();
            if (!_job.IsEmpty) throw new InvalidOperationException("Owned process tree is not empty.");
            _state = State.Stopped;
            return new StoppedLease(this, operation);
        }
        catch { operation.Dispose(); throw; }
    }

    public async ValueTask DisposeAsync()
    {
        // A lease holder may be the caller; reject promptly instead of deadlocking or invalidating its lease.
        if (!_operations.TryEnter(out var operation)) throw new InvalidOperationException("Release the active runtime operation or lease before disposal.");
        using (operation)
        {
            if (_state == State.Disposed) return;
            _state = State.Unknown;
            await StopCoreAsync(TimeSpan.FromSeconds(5), CancellationToken.None).ConfigureAwait(false);
            _job.Dispose();
            _directory.Dispose();
            _state = State.Disposed;
        }
    }

    private void ThrowIfDisposed() => ObjectDisposedException.ThrowIf(_state == State.Disposed, this);

    private sealed class StoppedLease(WindowsOwnedRuntimeSession owner, IDisposable operation) : IDisposable
    {
        private WindowsOwnedRuntimeSession? _owner = owner;
        private IDisposable? _operation = operation;
        public void Dispose()
        {
            var held = Interlocked.Exchange(ref _operation, null);
            if (held is null) return;
            held.Dispose();
            GC.KeepAlive(_owner);
            _owner = null;
        }
    }
}
