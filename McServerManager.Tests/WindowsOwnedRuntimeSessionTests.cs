using System.Diagnostics;
using System.IO;
using System.Runtime.CompilerServices;
using McServerManager.Services.RuntimeLifecycle;
using McServerManager.Services.WindowsOwnership;
using Xunit.Abstractions;

namespace McServerManager.Tests;

public sealed class WindowsOwnedRuntimeSessionTests(ITestOutputHelper output)
{
    private static readonly string Dotnet = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "dotnet", "dotnet.exe");
    private static readonly string Child = Path.Combine(AppContext.BaseDirectory, "OwnershipTestChild", "McServerManager.OwnershipTestChild.dll");
    private static string Sandbox() => Path.Combine(Path.GetTempPath(), "maipilot-owner-" + Guid.NewGuid().ToString("N"));
    private static WindowsOwnedRuntimeSession Create(string root, Func<IWindowsOwnedJob>? factory = null) =>
        WindowsOwnedRuntimeSession.CreateNew(root, new RuntimeOperationCoordinator(), factory ?? (() => new WindowsOwnedJob()));
    private static string[] Arguments(string mode, string root) => [Child, mode, root];

    private static async Task WaitForFile(string path)
    {
        var deadline = Stopwatch.StartNew();
        while (!File.Exists(path))
        {
            if (deadline.Elapsed > TimeSpan.FromSeconds(10)) throw new TimeoutException("Harmless process fixture did not signal: " + Path.GetFileName(path));
            await Task.Delay(20);
        }
    }

    [Fact]
    public async Task NativeDescendantSurvivesRootExitUntilWholeJobStopped()
    {
        var root = Sandbox();
        WindowsOwnedJob? job = null;
        await using (var session = Create(root, () => job = new WindowsOwnedJob()))
        {
            await session.StartAsync(Dotnet, Arguments("tree", root));
            await WaitForFile(Path.Combine(root, "child-ready"));
            await WaitForFile(Path.Combine(root, "root-exiting"));
            var rootPid = int.Parse(await File.ReadAllTextAsync(Path.Combine(root, "root-exiting")));
            try { using var exitedRoot = Process.GetProcessById(rootPid); await exitedRoot.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(10)); }
            catch (ArgumentException) { /* Already exited. */ }
            Assert.False(job!.IsEmpty);
            await Assert.ThrowsAsync<InvalidOperationException>(async () => { using var denied = await session.AcquireStoppedLeaseAsync(); });
            await session.StopAsync(TimeSpan.FromSeconds(5));
            Assert.True(job.IsEmpty);
            await session.StopAsync(TimeSpan.FromSeconds(5));
            using var stopped = await session.AcquireStoppedLeaseAsync();
        }
        Assert.ThrowsAny<Exception>(() => Create(root)); // Even clean historical owners cannot be adopted.
        Directory.Delete(root, true);
    }

    [Fact]
    public async Task LeaseExcludesStartAndDisposeAndDoubleDisposeIsSafe()
    {
        var root = Sandbox();
        await using (var session = Create(root))
        {
            await session.StopAsync(TimeSpan.FromSeconds(5));
            var lease = await session.AcquireStoppedLeaseAsync();
            using var cancellation = new CancellationTokenSource();
            var pending = session.StartAsync(Dotnet, Arguments("hold", root), cancellation.Token);
            Assert.False(pending.IsCompleted);
            await Assert.ThrowsAsync<InvalidOperationException>(() => session.DisposeAsync().AsTask());
            cancellation.Cancel();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => pending);
            lease.Dispose(); lease.Dispose();
            await session.StartAsync(Dotnet, Arguments("hold", root));
            await WaitForFile(Path.Combine(root, "child-ready"));
            await session.StopAsync(TimeSpan.FromSeconds(5));
        }
        Directory.Delete(root, true);
    }

    [Fact]
    public async Task ReservationExcludesAnotherProcessAndPinsRejectRename()
    {
        var parent = Sandbox(); Directory.CreateDirectory(parent);
        var root = Path.Combine(parent, "runtime");
        await using (var session = Create(root))
        {
            Assert.ThrowsAny<Exception>(() => Create(root));
            Assert.Throws<IOException>(() => Directory.Move(root, root + "-moved"));
            Assert.Throws<IOException>(() => Directory.Move(parent, parent + "-moved"));
            Assert.Throws<IOException>(() => File.Move(Path.Combine(root, WindowsOwnedDirectory.ReservationName), Path.Combine(root, "replaced")));
            await session.StartAsync(Dotnet, Arguments("lock", root));
            await WaitForFile(Path.Combine(root, "lock-blocked"));
            Assert.False(File.Exists(Path.Combine(root, "lock-unexpected")));
            await session.StopAsync(TimeSpan.FromSeconds(5));
        }
        Directory.Move(root, root + "-moved"); // Handles are genuinely released after confirmed disposal.
        Directory.Delete(parent, true);
    }

    [Fact]
    public async Task FailedStopRetainsReservationAndExplicitRetryRecovers()
    {
        var root = Sandbox();
        var job = new FaultJob(new WindowsOwnedJob());
        await using (var session = Create(root, () => job))
        {
            await session.StartAsync(Dotnet, Arguments("hold", root));
            await WaitForFile(Path.Combine(root, "child-ready"));
            job.FailTerminate = true;
            await Assert.ThrowsAsync<IOException>(() => session.StopAsync(TimeSpan.FromSeconds(5)));
            await Assert.ThrowsAsync<IOException>(() => session.DisposeAsync().AsTask());
            await Assert.ThrowsAsync<InvalidOperationException>(async () => { using var denied = await session.AcquireStoppedLeaseAsync(); });
            Assert.Throws<IOException>(() => File.Open(Path.Combine(root, WindowsOwnedDirectory.ReservationName), FileMode.Open, FileAccess.ReadWrite, FileShare.None));
            await Assert.ThrowsAsync<InvalidOperationException>(() => session.StartAsync(Dotnet, Arguments("hold", root)));
            job.FailTerminate = false;
            await session.StopAsync(TimeSpan.FromSeconds(5));
            using var lease = await session.AcquireStoppedLeaseAsync();
        }
        Directory.Delete(root, true);
    }

    [Fact]
    public async Task CancellationAfterNativeCreationAndDuringStopNeverGrantsLease()
    {
        var root = Sandbox();
        var job = new FaultJob(new WindowsOwnedJob());
        await using (var session = Create(root, () => job))
        {
            using var startCancellation = new CancellationTokenSource();
            job.AfterStart = () => { startCancellation.Cancel(); startCancellation.Token.ThrowIfCancellationRequested(); };
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => session.StartAsync(Dotnet, Arguments("hold", root), startCancellation.Token));
            await Assert.ThrowsAsync<InvalidOperationException>(async () => { using var denied = await session.AcquireStoppedLeaseAsync(); });
            job.AfterStart = null;
            await session.StopAsync(TimeSpan.FromSeconds(5));
            await session.StartAsync(Dotnet, Arguments("hold", root));
            using var stopCancellation = new CancellationTokenSource();
            job.AfterTerminate = stopCancellation.Cancel;
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => session.StopAsync(TimeSpan.FromSeconds(5), stopCancellation.Token));
            await Assert.ThrowsAsync<InvalidOperationException>(async () => { using var denied = await session.AcquireStoppedLeaseAsync(); });
            job.AfterTerminate = null;
            await session.StopAsync(TimeSpan.FromSeconds(5));
        }
        Directory.Delete(root, true);
    }

    [Fact]
    public async Task StatusFailureAndTimeoutKeepUnknownUntilRetry()
    {
        var root = Sandbox();
        var job = new FaultJob(new WindowsOwnedJob());
        await using (var session = Create(root, () => job))
        {
            await session.StartAsync(Dotnet, Arguments("hold", root));
            job.FailQuery = true;
            await Assert.ThrowsAsync<IOException>(() => session.StopAsync(TimeSpan.FromSeconds(5)));
            job.FailQuery = false; job.ReportNonempty = true;
            await Assert.ThrowsAsync<TimeoutException>(() => session.StopAsync(TimeSpan.FromMilliseconds(30)));
            await Assert.ThrowsAsync<InvalidOperationException>(async () => { using var denied = await session.AcquireStoppedLeaseAsync(); });
            job.ReportNonempty = false;
            await session.StopAsync(TimeSpan.FromSeconds(5));
        }
        Directory.Delete(root, true);
    }

    [Fact]
    public async Task ArgumentsAreNotInterpretedByShellAndPreCancelledStartDoesNotLaunch()
    {
        var root = Sandbox();
        await using (var session = Create(root))
        {
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => session.StartAsync(Dotnet, Arguments("hold", root), new CancellationToken(true)));
            Assert.False(File.Exists(Path.Combine(root, "child-ready")));
            string[] values = ["with space", "embedded\"quote", "trailing\\", "", "& whoami > forbidden", "日本語"];
            await session.StartAsync(Dotnet, [Child, "echo", root, .. values]);
            await WaitForFile(Path.Combine(root, "arguments.txt"));
            Assert.Equal(values, await File.ReadAllLinesAsync(Path.Combine(root, "arguments.txt")));
            Assert.False(File.Exists(Path.Combine(root, "forbidden")));
            await session.StopAsync(TimeSpan.FromSeconds(5));
        }
        Directory.Delete(root, true);
    }

    [Theory]
    [InlineData("relative")]
    [InlineData("C:\\temp\\..\\unsafe")]
    [InlineData("\\\\server\\share\\unsafe")]
    [InlineData("C:\\unsafe:stream")]
    [InlineData("C:\\unsafe.")]
    [InlineData("C:\\NUL")]
    public void AmbiguousAndExternalDirectoryPathsAreRejected(string path) => Assert.ThrowsAny<ArgumentException>(() => Create(path));

    [Fact]
    public async Task CancellationWhileNativeRootIsSuspendedDoesNotRunChild()
    {
        var root = Sandbox();
        using var cancellation = new CancellationTokenSource();
        var observed = false;
        await using (var session = Create(root, () => new WindowsOwnedJob(() => { observed = true; cancellation.Cancel(); })))
        {
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => session.StartAsync(Dotnet, Arguments("hold", root), cancellation.Token));
            Assert.True(observed);
            Assert.False(File.Exists(Path.Combine(root, "child-ready")));
            await Assert.ThrowsAsync<InvalidOperationException>(async () => { using var denied = await session.AcquireStoppedLeaseAsync(); });
            await session.StopAsync(TimeSpan.FromSeconds(5));
            using var lease = await session.AcquireStoppedLeaseAsync();
            Assert.False(File.Exists(Path.Combine(root, "child-ready")));
        }
        Directory.Delete(root, true);
    }

    [Fact]
    public async Task LeaseKeepsNativeOwnerAliveAcrossGarbageCollection()
    {
        var root = Sandbox();
        var (owner, lease) = MakeUnrootedOwnerWithLease(root);
        GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect();
        Assert.True(owner.TryGetTarget(out var retained));
        Assert.Throws<IOException>(() => Directory.Move(root, root + "-moved"));
        Assert.Throws<IOException>(() => File.Open(Path.Combine(root, WindowsOwnedDirectory.ReservationName), FileMode.Open, FileAccess.ReadWrite, FileShare.None));
        lease.Dispose();
        await retained!.DisposeAsync();
        Directory.Delete(root, true);
    }

    [Fact]
    public async Task ActualOwnerProcessCrashKillsItsJobAndNeverAuthorizesAdoption()
    {
        var root = Sandbox(); Directory.CreateDirectory(root);
        var runtime = Path.Combine(root, "runtime");
        using (var containment = new WindowsOwnedJob())
        {
            // This outer Job is retained throughout observation. It supplies cleanup on test failure,
            // but is never terminated/closed to obtain the asserted inner-owner crash result.
            containment.Start(Dotnet, Arguments("owner-crash", root), root, CancellationToken.None);
            await WaitForFile(Path.Combine(root, "owner-ready"));
            Assert.False(containment.IsEmpty);
            Assert.Throws<IOException>(() => File.Open(Path.Combine(runtime, WindowsOwnedDirectory.ReservationName), FileMode.Open, FileAccess.ReadWrite, FileShare.None));
            await File.WriteAllTextAsync(Path.Combine(root, "crash-now"), "terminate fixture owner only");
            var deadline = Stopwatch.StartNew();
            while (!containment.IsEmpty)
            {
                if (deadline.Elapsed > TimeSpan.FromSeconds(10)) throw new TimeoutException("Crashed owner's native descendants remained alive.");
                await Task.Delay(20);
            }
            // Crash released the reservation. Neither that fact nor job cleanup permits adoption.
            using (File.Open(Path.Combine(runtime, WindowsOwnedDirectory.ReservationName), FileMode.Open, FileAccess.ReadWrite, FileShare.None)) { }
            var refusal = Assert.Throws<System.ComponentModel.Win32Exception>(() => Create(runtime));
            Assert.Equal(183, refusal.NativeErrorCode); // ERROR_ALREADY_EXISTS
            output.WriteLine("Owner killed itself without Stop/Dispose; retained outer Job became empty; released reservation did not authorize adoption (183).");
        }
        Directory.Delete(root, true);
    }

    [Fact]
    public async Task ExplicitBreakawayIsDeniedWhileOrdinarySuspendedChildIsContained()
    {
        var root = Sandbox();
        await using (var session = Create(root))
        {
            await session.StartAsync(Dotnet, Arguments("breakaway", root));
            await WaitForFile(Path.Combine(root, "breakaway-result"));
            Assert.Equal("controlCreated=True;controlInCleanupJob=True;escapeCreated=False;escapeError=5",
                await File.ReadAllTextAsync(Path.Combine(root, "breakaway-result")));
            output.WriteLine("Ordinary suspended child: created and in its cleanup Job. CREATE_BREAKAWAY_FROM_JOB: denied with Win32 5. Both attempts atomically assign a cleanup Job; this result includes any enclosing Job restrictions.");
            await session.StopAsync(TimeSpan.FromSeconds(5));
            using var stopped = await session.AcquireStoppedLeaseAsync();
        }
        Directory.Delete(root, true);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static (WeakReference<WindowsOwnedRuntimeSession>, IDisposable) MakeUnrootedOwnerWithLease(string root)
    {
        var owner = Create(root);
        owner.StopAsync(TimeSpan.FromSeconds(5)).GetAwaiter().GetResult();
        return (new WeakReference<WindowsOwnedRuntimeSession>(owner), owner.AcquireStoppedLeaseAsync().AsTask().GetAwaiter().GetResult());
    }

    private sealed class FaultJob(IWindowsOwnedJob native) : IWindowsOwnedJob
    {
        internal bool FailTerminate, FailQuery, ReportNonempty;
        internal Action? AfterStart, AfterTerminate;
        public bool IsEmpty => FailQuery ? throw new IOException("Injected status failure") : !ReportNonempty && native.IsEmpty;
        public void Start(string executable, IReadOnlyList<string> arguments, string directory, CancellationToken cancellationToken)
        { native.Start(executable, arguments, directory, cancellationToken); AfterStart?.Invoke(); }
        public void Terminate() { if (FailTerminate) throw new IOException("Injected termination failure"); native.Terminate(); AfterTerminate?.Invoke(); }
        public void Dispose() => native.Dispose();
    }
}
