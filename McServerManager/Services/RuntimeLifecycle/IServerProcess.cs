using System.Diagnostics;
using System.Threading;

namespace McServerManager.Services.RuntimeLifecycle;

/// <summary>An owned launch handle; fakes must not start operating-system processes.</summary>
public interface IServerProcess : IDisposable
{
    event Action<string?>? OutputReceived;
    event Action? Exited;
    Process? NativeProcess { get; }
    int ExitCode { get; }
    /// <summary>False or Win32Exception must mean native creation failed with no launched child.
    /// Other exceptions are treated as an uncertain launch and retain this handle.</summary>
    bool Start();
    void BeginOutputRead();
    Task SendCommandAsync(string command);
    Task<bool> WaitForExitAsync(TimeSpan timeout, CancellationToken cancellationToken = default);
    void KillTree();
}

public interface IServerProcessFactory
{
    IServerProcess Create(ProcessStartInfo startInfo);
}
