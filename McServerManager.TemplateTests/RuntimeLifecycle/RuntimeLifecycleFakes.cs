using System.Collections.Concurrent;
using System.Diagnostics;
using McServerManager.Models;
using McServerManager.Models.RuntimeLifecycle;
using McServerManager.Services;
using McServerManager.Services.RuntimeLifecycle;

namespace McServerManager.TemplateTests.RuntimeLifecycle;

/// <summary>Only synthetic handles: no helper in this file creates a native process.</summary>
internal sealed class FakeServerProcess : IServerProcess
{
    private int _startCalls;
    private int _waitCalls;
    private int _killCalls;
    private int _disposeCalls;

    public event Action<string?>? OutputReceived;
    public event Action? Exited;
    public Process? NativeProcess => null;
    public int ExitCode { get; private set; }
    public int StartCalls => Volatile.Read(ref _startCalls);
    public int WaitCalls => Volatile.Read(ref _waitCalls);
    public int KillCalls => Volatile.Read(ref _killCalls);
    public int DisposeCalls => Volatile.Read(ref _disposeCalls);
    public bool CompleteStartupOnRead { get; set; }
    public Func<bool>? StartBehavior { get; set; }
    public Func<TimeSpan, CancellationToken, Task<bool>>? WaitBehavior { get; set; }
    public Action? KillBehavior { get; set; }
    public ConcurrentQueue<string> Commands { get; } = new();
    public TaskCompletionSource StartEntered { get; } = NewSignal();

    public bool Start()
    {
        Interlocked.Increment(ref _startCalls);
        StartEntered.TrySetResult();
        return StartBehavior?.Invoke() ?? true;
    }

    public void BeginOutputRead()
    {
        if (CompleteStartupOnRead)
            EmitOutput("Done (0.001s)! For help, type help");
    }

    public Task SendCommandAsync(string command)
    {
        Commands.Enqueue(command);
        return Task.CompletedTask;
    }

    public Task<bool> WaitForExitAsync(TimeSpan timeout, CancellationToken cancellationToken = default)
    {
        Interlocked.Increment(ref _waitCalls);
        return WaitBehavior?.Invoke(timeout, cancellationToken) ?? Task.FromResult(true);
    }

    public void KillTree()
    {
        Interlocked.Increment(ref _killCalls);
        KillBehavior?.Invoke();
    }

    public void EmitOutput(string? text) => OutputReceived?.Invoke(text);

    public void EmitExit(int exitCode = 0)
    {
        ExitCode = exitCode;
        Exited?.Invoke();
    }

    public (Action<string?>? Output, Action? Exit) CaptureCallbacks() => (OutputReceived, Exited);
    public void Dispose() => Interlocked.Increment(ref _disposeCalls);
    public static TaskCompletionSource NewSignal() => new(TaskCreationOptions.RunContinuationsAsynchronously);
}

internal sealed class FakeServerProcessFactory : IServerProcessFactory
{
    private readonly ConcurrentQueue<FakeServerProcess> _pending = new();
    private readonly ConcurrentQueue<FakeServerProcess> _created = new();
    private readonly ConcurrentQueue<ProcessStartInfo> _startInfos = new();

    public int CreateCalls => _created.Count;
    public FakeServerProcess[] Created => _created.ToArray();
    public ProcessStartInfo[] StartInfos => _startInfos.ToArray();
    public void Enqueue(FakeServerProcess process) => _pending.Enqueue(process);

    public IServerProcess Create(ProcessStartInfo startInfo)
    {
        var process = _pending.TryDequeue(out var supplied) ? supplied : new FakeServerProcess();
        _startInfos.Enqueue(startInfo);
        _created.Enqueue(process);
        return process;
    }
}

internal sealed class FakeProcessTreeTerminator : IProcessTreeTerminator
{
    private int _stopCalls;
    public int StopCalls => Volatile.Read(ref _stopCalls);
    public ConcurrentQueue<IServerProcess> Targets { get; } = new();
    public TaskCompletionSource StopEntered { get; } = FakeServerProcess.NewSignal();
    public Func<IServerProcess, TimeSpan, CancellationToken, Task<ProcessStopResult>>? StopBehavior { get; set; }

    public Task<ProcessStopResult> StopAsync(
        IServerProcess process, TimeSpan timeout, CancellationToken cancellationToken = default)
    {
        Interlocked.Increment(ref _stopCalls);
        Targets.Enqueue(process);
        StopEntered.TrySetResult();
        return StopBehavior?.Invoke(process, timeout, cancellationToken)
            ?? Task.FromResult(new ProcessStopResult(ProcessStopOutcome.RootExitedUnverified, false));
    }
}

/// <summary>Uses the real exclusion gate, with deterministic observation of queued operations.</summary>
internal sealed class ObservedRuntimeOperationCoordinator : IRuntimeOperationCoordinator
{
    private readonly RuntimeOperationCoordinator _inner = new();
    private readonly object _sync = new();
    private readonly List<(int Count, TaskCompletionSource Signal)> _waiters = [];
    private int _enterCalls;

    public int EnterCalls { get { lock (_sync) return _enterCalls; } }

    public ValueTask<IDisposable> EnterAsync(CancellationToken cancellationToken = default)
    {
        lock (_sync)
        {
            _enterCalls++;
            foreach (var waiter in _waiters.Where(waiter => waiter.Count <= _enterCalls).ToArray())
            {
                waiter.Signal.TrySetResult();
                _waiters.Remove(waiter);
            }
        }
        return _inner.EnterAsync(cancellationToken);
    }

    public bool TryEnter(out IDisposable? lease) => _inner.TryEnter(out lease);
    public ValueTask<IDisposable> HoldAsync() => _inner.EnterAsync();

    public Task WaitForEnterCallsAsync(int count)
    {
        lock (_sync)
        {
            if (_enterCalls >= count)
                return Task.CompletedTask;
            var signal = FakeServerProcess.NewSignal();
            _waiters.Add((count, signal));
            return signal.Task.WaitAsync(RuntimeTestDirectory.Timeout);
        }
    }
}

internal sealed class RuntimeTestDirectory : IDisposable
{
    public static TimeSpan Timeout { get; } = TimeSpan.FromSeconds(10);
    public string Root { get; } = Path.Combine(Path.GetTempPath(), "maipilot-runtime-test-" + Guid.NewGuid().ToString("N"));

    public RuntimeTestDirectory()
    {
        Directory.CreateDirectory(Root);
        // An empty marker satisfies launch-plan validation. It is never executed.
        File.WriteAllBytes(Path.Combine(Root, "server.jar"), []);
    }

    public ServerConfig CreateConfig(string serverId = "synthetic-server") => new()
    {
        ServerId = serverId,
        Name = "Synthetic runtime test",
        DirectoryPath = Root,
        AutoRestartOnCrash = false,
        AutoRestartDelaySeconds = 1
    };

    public void AddShellWrapper() => File.WriteAllText(Path.Combine(Root, "run.bat"), string.Empty);
    public void Dispose() => Directory.Delete(Root, recursive: true);
}

/// <summary>A controllable timer. Cancellation can deliberately lose a race with delivery.</summary>
internal sealed class ManualRuntimeDelay
{
    private int _calls;
    public int Calls => Volatile.Read(ref _calls);
    public bool IgnoreCancellation { get; init; }
    public TaskCompletionSource<DelayRequest> Scheduled { get; } =
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    public async Task DelayAsync(TimeSpan duration, CancellationToken token)
    {
        Interlocked.Increment(ref _calls);
        var request = new DelayRequest(duration, token);
        using var registration = token.Register(() => request.CancellationObserved.TrySetResult());
        Scheduled.TrySetResult(request);
        if (IgnoreCancellation)
            await request.Release.Task.ConfigureAwait(false);
        else
            await request.Release.Task.WaitAsync(token).ConfigureAwait(false);
    }

    internal sealed record DelayRequest(TimeSpan Duration, CancellationToken Token)
    {
        public TaskCompletionSource Release { get; } = FakeServerProcess.NewSignal();
        public TaskCompletionSource CancellationObserved { get; } = FakeServerProcess.NewSignal();
    }
}
