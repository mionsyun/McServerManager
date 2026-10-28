using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using static McServerManager.Services.WindowsOwnership.WindowsOwnershipNative;

namespace McServerManager.Services.WindowsOwnership;

internal interface IWindowsOwnedJob : IDisposable
{
    void Start(string executable, IReadOnlyList<string> arguments, string directory, CancellationToken cancellationToken);
    bool IsEmpty { get; }
    void Terminate();
}

/// <summary>Ordinary descendants only; brokered/service launches are outside this ownership model.</summary>
internal sealed class WindowsOwnedJob : IWindowsOwnedJob
{
    private readonly KernelHandle _job;
    private KernelHandle? _root;
    private readonly Action? _onSuspended;

    // Internal observer makes the native pre-resume cancellation boundary deterministic in tests.
    internal WindowsOwnedJob(Action? onSuspended = null)
    {
        _onSuspended = onSuspended;
        _job = CreateJobObjectW(IntPtr.Zero, IntPtr.Zero);
        if (_job.IsInvalid) { _job.Dispose(); throw Error(); }
        var limits = new ExtendedLimits { Basic = new BasicLimits { Flags = 0x2000 } }; // KILL_ON_JOB_CLOSE, no breakaway
        if (!SetInformationJobObject(_job, 9, ref limits, (uint)Marshal.SizeOf<ExtendedLimits>()))
        {
            var error = Error(); _job.Dispose(); throw error;
        }
    }

    public bool IsEmpty
    {
        get
        {
            if (!QueryInformationJobObject(_job, 1, out var accounting, (uint)Marshal.SizeOf<BasicAccounting>(), IntPtr.Zero)) throw Error();
            if (accounting.Active != 0) return false;
            if (_root is null) return true;
            var wait = WaitForSingleObject(_root, 0);
            if (wait == uint.MaxValue) throw Error();
            return wait == 0;
        }
    }

    public void Start(string executable, IReadOnlyList<string> arguments, string directory, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!Path.IsPathFullyQualified(executable) || !File.Exists(executable)) throw new ArgumentException("An existing absolute executable path is required.", nameof(executable));
        if (!IsEmpty) throw new InvalidOperationException("Owned job is not empty.");
        var command = new StringBuilder(Quote(executable));
        foreach (var argument in arguments) command.Append(' ').Append(Quote(argument));
        UIntPtr size = UIntPtr.Zero;
        InitializeProcThreadAttributeList(IntPtr.Zero, 1, 0, ref size);
        var attributes = Marshal.AllocHGlobal(checked((int)size.ToUInt64()));
        var jobList = Marshal.AllocHGlobal(IntPtr.Size);
        var initialized = false;
        try
        {
            if (!InitializeProcThreadAttributeList(attributes, 1, 0, ref size)) throw Error();
            initialized = true;
            Marshal.WriteIntPtr(jobList, _job.DangerousGetHandle());
            // JOB_LIST assigns ownership atomically with creation (Windows 10+). No unowned suspended interval.
            if (!UpdateProcThreadAttribute(attributes, 0, (UIntPtr)0x2000D, jobList, (UIntPtr)IntPtr.Size, IntPtr.Zero, IntPtr.Zero)) throw Error();
            var startup = new StartupInfoEx { Startup = new StartupInfo { Size = Marshal.SizeOf<StartupInfoEx>() }, Attributes = attributes };
            cancellationToken.ThrowIfCancellationRequested();
            if (!CreateProcessW(executable, command, IntPtr.Zero, IntPtr.Zero, false, 0x08080004, IntPtr.Zero, directory, ref startup, out var process)) throw Error();
            _root?.Dispose();
            _root = new KernelHandle(process.Process);
            using var thread = new KernelHandle(process.Thread);
            _onSuspended?.Invoke();
            cancellationToken.ThrowIfCancellationRequested();
            if (ResumeThread(thread) == uint.MaxValue) throw Error();
        }
        finally
        {
            if (initialized) DeleteProcThreadAttributeList(attributes);
            Marshal.FreeHGlobal(jobList);
            Marshal.FreeHGlobal(attributes);
            GC.KeepAlive(_job);
        }
    }

    private static string Quote(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        if (value.Contains('\0')) throw new ArgumentException("NUL is not a command argument.");
        var result = new StringBuilder("\"");
        var slashes = 0;
        foreach (var character in value)
        {
            if (character == '\\') { slashes++; continue; }
            result.Append('\\', character == '"' ? slashes * 2 + 1 : slashes);
            result.Append(character); slashes = 0;
        }
        return result.Append('\\', slashes * 2).Append('"').ToString();
    }

    public void Terminate() { if (!TerminateJobObject(_job, 1)) throw Error(); }
    public void Dispose() { _root?.Dispose(); _job.Dispose(); }
}
