using System.Diagnostics;
using McServerManager.Models.RuntimeLifecycle;
using McServerManager.Services.RuntimeLifecycle;

namespace McServerManager.TemplateTests.RuntimeLifecycle;

public sealed class ProcessTreeTerminatorTests
{
    [Fact]
    public async Task GracefulRootExitRemainsUnverifiedTreeExit()
    {
        using var process = new StopProcess(true);
        var result = await new ProcessTreeTerminator().StopAsync(process, TimeSpan.FromSeconds(1));
        Assert.Equal(ProcessStopOutcome.RootExitedUnverified, result.Outcome);
        Assert.False(result.Forced);
        Assert.Equal(0, process.KillCalls);
        Assert.Equal(new[] { "stop" }, process.Commands);
    }

    [Fact]
    public async Task ForcedRootExitStillDoesNotCertifyDescendants()
    {
        using var process = new StopProcess(false, true);
        var result = await new ProcessTreeTerminator().StopAsync(process, TimeSpan.Zero);
        Assert.Equal(ProcessStopOutcome.RootExitedUnverified, result.Outcome);
        Assert.True(result.Forced);
        Assert.Equal(1, process.KillCalls);
        Assert.Equal(TimeSpan.FromSeconds(1), process.Waits.Last());
    }

    [Fact]
    public async Task KillFailureIsUncertain()
    {
        using var process = new StopProcess(false) { KillError = new InvalidOperationException("kill failed") };
        var result = await new ProcessTreeTerminator().StopAsync(process, TimeSpan.Zero);
        Assert.Equal(ProcessStopOutcome.Uncertain, result.Outcome);
        Assert.Equal("kill failed", result.Error);
        Assert.True(result.Forced);
    }

    [Fact]
    public async Task ForcedWaitTimeoutIsUncertain()
    {
        using var process = new StopProcess(false, false);
        var result = await new ProcessTreeTerminator().StopAsync(process, TimeSpan.Zero);
        Assert.Equal(ProcessStopOutcome.Uncertain, result.Outcome);
        Assert.NotNull(result.Error);
        Assert.True(result.Forced);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task WaitFailureIsUncertain(bool afterKill)
    {
        using var process = new StopProcess(false) { FailWaitNumber = afterKill ? 2 : 1 };
        var result = await new ProcessTreeTerminator().StopAsync(process, TimeSpan.Zero);
        Assert.Equal(ProcessStopOutcome.Uncertain, result.Outcome);
        Assert.Equal(afterKill ? 1 : 0, process.KillCalls);
        Assert.Contains("wait failed", result.Error);
    }

    [Fact]
    public async Task BrokenInputDoesNotPreventConfirmedRootWait()
    {
        using var process = new StopProcess(true) { CommandError = new IOException("stdin closed") };
        var result = await new ProcessTreeTerminator().StopAsync(process, TimeSpan.Zero);
        Assert.Equal(ProcessStopOutcome.RootExitedUnverified, result.Outcome);
    }

    [Fact]
    public async Task BlockedInputDoesNotPreventBoundedWaitAndKill()
    {
        using var process = new StopProcess(false, true) { BlockCommand = true };
        var result = await new ProcessTreeTerminator().StopAsync(process, TimeSpan.Zero);
        Assert.Equal(ProcessStopOutcome.RootExitedUnverified, result.Outcome);
        Assert.Equal(1, process.KillCalls);
    }

    [Fact]
    public async Task CancellationDoesNotClaimStoppedOrAttemptKill()
    {
        using var process = new StopProcess(false);
        using var source = new CancellationTokenSource();
        source.Cancel();
        var result = await new ProcessTreeTerminator().StopAsync(process, TimeSpan.Zero, source.Token);
        Assert.Equal(ProcessStopOutcome.Uncertain, result.Outcome);
        Assert.Empty(process.Commands);
        Assert.Equal(0, process.KillCalls);
    }

    private sealed class StopProcess(params bool[] waitResults) : IServerProcess
    {
        private readonly Queue<bool> _waitResults = new(waitResults);
        public event Action<string?>? OutputReceived { add { } remove { } }
        public event Action? Exited { add { } remove { } }
        public Process? NativeProcess => null;
        public int ExitCode => 0;
        public List<string> Commands { get; } = new();
        public List<TimeSpan> Waits { get; } = new();
        public int KillCalls { get; private set; }
        public Exception? KillError { get; init; }
        public Exception? CommandError { get; init; }
        public int FailWaitNumber { get; init; }
        public bool BlockCommand { get; init; }
        public bool Start() => throw new NotSupportedException("Synthetic stop-only process");
        public void BeginOutputRead() => throw new NotSupportedException();
        public Task SendCommandAsync(string command)
        {
            Commands.Add(command);
            if (CommandError is not null) return Task.FromException(CommandError);
            return BlockCommand ? new TaskCompletionSource().Task : Task.CompletedTask;
        }
        public Task<bool> WaitForExitAsync(TimeSpan timeout, CancellationToken cancellationToken = default)
        {
            Waits.Add(timeout);
            if (Waits.Count == FailWaitNumber)
                throw new IOException("wait failed");
            return Task.FromResult(_waitResults.Dequeue());
        }
        public void KillTree()
        {
            KillCalls++;
            if (KillError is not null) throw KillError;
        }
        public void Dispose() { }
    }
}
