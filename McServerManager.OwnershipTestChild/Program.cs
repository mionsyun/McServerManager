using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using McServerManager.Services.WindowsOwnership;

// Fixture-only executable: no network, server/JAR execution, or writes outside the supplied sandbox.
if (args.Length < 2) return 2;
var mode = args[0];
var root = args[1];
void Publish(string name, string value)
{
    var destination = Path.Combine(root, name);
    File.WriteAllText(destination + ".pending", value);
    File.Move(destination + ".pending", destination);
}
if (mode == "lock")
{
    FileStream stream;
    try
    {
        stream = new FileStream(Path.Combine(root, ".maipilot-owned-runtime"), FileMode.Open, FileAccess.ReadWrite, FileShare.None);
    }
    catch (IOException) { Publish("lock-blocked", "blocked"); return 0; }
    using (stream) Publish("lock-unexpected", "opened");
    return 3;
}
if (mode == "echo")
{
    File.WriteAllLines(Path.Combine(root, "arguments.pending"), args.Skip(2));
    File.Move(Path.Combine(root, "arguments.pending"), Path.Combine(root, "arguments.txt"));
    return 0;
}
if (mode == "tree")
{
    var start = new ProcessStartInfo(Environment.ProcessPath!) { UseShellExecute = false, CreateNoWindow = true };
    start.ArgumentList.Add(Assembly.GetExecutingAssembly().Location);
    start.ArgumentList.Add("hold");
    start.ArgumentList.Add(root);
    using var child = Process.Start(start) ?? throw new InvalidOperationException("Fixture child did not start.");
    Publish("root-exiting", Environment.ProcessId.ToString());
    return 0;
}
if (mode == "owner-crash")
{
    var runtime = Path.Combine(root, "runtime");
    var session = WindowsOwnedRuntimeSession.CreateNew(runtime);
    await session.StartAsync(Environment.ProcessPath!, [Assembly.GetExecutingAssembly().Location, "hold", runtime]);
    var deadline = Stopwatch.StartNew();
    while (!File.Exists(Path.Combine(runtime, "child-ready")))
    {
        if (deadline.Elapsed > TimeSpan.FromSeconds(10)) throw new TimeoutException("Owned fixture child did not start.");
        await Task.Delay(10);
    }
    Publish("owner-ready", "inner session owns a live child");
    while (!File.Exists(Path.Combine(root, "crash-now"))) await Task.Delay(10);
    // Abruptly terminate only this fixture owner. No Dispose/finally/Stop is run.
    Process.GetCurrentProcess().Kill();
    GC.KeepAlive(session); // Keep handles live through Kill, preventing a finalizer-cleanup false positive.
    await Task.Delay(Timeout.InfiniteTimeSpan);
    return 5;
}
if (mode == "breakaway")
{
    var ordinary = BreakawayProbe.Attempt(false, root);
    var escape = BreakawayProbe.Attempt(true, root);
    Publish("breakaway-result", $"controlCreated={ordinary.Created};controlInCleanupJob={ordinary.InJob};escapeCreated={escape.Created};escapeError={escape.Error}");
    return 0;
}
if (mode != "hold") return 4;
Publish("child-ready", Environment.ProcessId.ToString());
await Task.Delay(Timeout.InfiniteTimeSpan);
return 0;

internal static class BreakawayProbe
{
    internal static (bool Created, bool InJob, int Error) Attempt(bool breakaway, string directory)
    {
        using var cleanup = CreateJobObjectW(IntPtr.Zero, IntPtr.Zero);
        if (cleanup.IsInvalid) throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
        var limits = new ExtendedLimits { Basic = new BasicLimits { Flags = 0x2000 } };
        if (!SetInformationJobObject(cleanup, 9, ref limits, (uint)Marshal.SizeOf<ExtendedLimits>())) throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
        UIntPtr size = UIntPtr.Zero;
        InitializeProcThreadAttributeList(IntPtr.Zero, 1, 0, ref size);
        var attributes = Marshal.AllocHGlobal(checked((int)size.ToUInt64()));
        var jobs = Marshal.AllocHGlobal(IntPtr.Size);
        var initialized = false;
        try
        {
            if (!InitializeProcThreadAttributeList(attributes, 1, 0, ref size)) throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
            initialized = true;
            Marshal.WriteIntPtr(jobs, cleanup.DangerousGetHandle());
            if (!UpdateProcThreadAttribute(attributes, 0, (UIntPtr)0x2000D, jobs, (UIntPtr)IntPtr.Size, IntPtr.Zero, IntPtr.Zero)) throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
            var startup = new StartupInfoEx { Startup = new StartupInfo { Size = Marshal.SizeOf<StartupInfoEx>() }, Attributes = attributes };
            var executable = Environment.ProcessPath!;
            // No child user code is ever resumed, even if the forbidden attempt unexpectedly succeeds.
            // JOB_LIST atomically assigns a separate kill-on-close cleanup Job before any child can exist.
            var created = CreateProcessW(executable, new StringBuilder('"' + executable + '"'), IntPtr.Zero, IntPtr.Zero, false,
                0x08080004u | (breakaway ? 0x01000000u : 0), IntPtr.Zero, directory, ref startup, out var process);
            if (!created) return (false, false, Marshal.GetLastWin32Error());
            try
            {
                if (!IsProcessInJob(process.Process, cleanup.DangerousGetHandle(), out var inJob)) throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
                return (true, inJob, 0);
            }
            finally
            {
                try
                {
                    if (!TerminateProcess(process.Process, 1) && WaitForSingleObject(process.Process, 0) != 0) throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
                    if (WaitForSingleObject(process.Process, 5000) != 0) throw new TimeoutException("Suspended probe child did not exit.");
                }
                finally { CloseHandle(process.Thread); CloseHandle(process.Process); }
            }
        }
        finally
        {
            if (initialized) DeleteProcThreadAttributeList(attributes);
            Marshal.FreeHGlobal(jobs); Marshal.FreeHGlobal(attributes);
            GC.KeepAlive(cleanup);
        }
    }

    [StructLayout(LayoutKind.Sequential)] private struct StartupInfo
    {
        internal int Size;
        internal IntPtr Reserved, Desktop, Title;
        internal uint X, Y, XSize, YSize, XChars, YChars, Fill, Flags;
        internal ushort Show, ReservedSize;
        internal IntPtr ReservedBytes, Input, Output, Error;
    }
    [StructLayout(LayoutKind.Sequential)] private struct ProcessInfo { internal IntPtr Process, Thread; internal uint ProcessId, ThreadId; }
    [StructLayout(LayoutKind.Sequential)] private struct StartupInfoEx { internal StartupInfo Startup; internal IntPtr Attributes; }
    [StructLayout(LayoutKind.Sequential)] private struct BasicLimits
    {
        internal long ProcessTime, JobTime;
        internal uint Flags;
        internal UIntPtr MinWorkingSet, MaxWorkingSet;
        internal uint ActiveLimit;
        internal UIntPtr Affinity;
        internal uint Priority, Scheduling;
    }
    [StructLayout(LayoutKind.Sequential)] private struct ExtendedLimits
    {
        internal BasicLimits Basic;
        internal ulong ReadOperations, WriteOperations, OtherOperations, ReadBytes, WriteBytes, OtherBytes;
        internal UIntPtr ProcessMemory, JobMemory, PeakProcessMemory, PeakJobMemory;
    }
    private sealed class JobHandle : Microsoft.Win32.SafeHandles.SafeHandleZeroOrMinusOneIsInvalid
    {
        public JobHandle() : base(true) { }
        protected override bool ReleaseHandle() => CloseHandle(handle);
    }
    [DllImport("kernel32.dll", SetLastError = true)] private static extern JobHandle CreateJobObjectW(IntPtr security, IntPtr name);
    [DllImport("kernel32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool SetInformationJobObject(JobHandle job, int kind, ref ExtendedLimits info, uint size);
    [DllImport("kernel32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool InitializeProcThreadAttributeList(IntPtr list, int count, uint flags, ref UIntPtr size);
    [DllImport("kernel32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool UpdateProcThreadAttribute(IntPtr list, uint flags, UIntPtr attribute, IntPtr value, UIntPtr size, IntPtr previous, IntPtr returned);
    [DllImport("kernel32.dll")] private static extern void DeleteProcThreadAttributeList(IntPtr list);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CreateProcessW(string application, StringBuilder command, IntPtr processSecurity, IntPtr threadSecurity, [MarshalAs(UnmanagedType.Bool)] bool inherit, uint flags, IntPtr environment, string directory, ref StartupInfoEx startup, out ProcessInfo process);
    [DllImport("kernel32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool IsProcessInJob(IntPtr process, IntPtr job, [MarshalAs(UnmanagedType.Bool)] out bool result);
    [DllImport("kernel32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool TerminateProcess(IntPtr process, uint code);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern uint WaitForSingleObject(IntPtr handle, uint milliseconds);
    [DllImport("kernel32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool CloseHandle(IntPtr handle);
}
