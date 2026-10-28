using System.Diagnostics;
using System.Threading;

namespace McServerManager.Services.RuntimeLifecycle;

public sealed class SystemServerProcessFactory : IServerProcessFactory
{
    public IServerProcess Create(ProcessStartInfo startInfo) => new SystemServerProcess(startInfo);

    private sealed class SystemServerProcess : IServerProcess
    {
        private readonly Process _process;

        public SystemServerProcess(ProcessStartInfo startInfo)
        {
            _process = new Process { StartInfo = startInfo, EnableRaisingEvents = true };
            _process.OutputDataReceived += (_, args) => OutputReceived?.Invoke(args.Data);
            _process.ErrorDataReceived += (_, args) => OutputReceived?.Invoke(args.Data);
            _process.Exited += (_, _) => Exited?.Invoke();
        }

        public event Action<string?>? OutputReceived;
        public event Action? Exited;
        public Process NativeProcess => _process;
        public int ExitCode => _process.ExitCode;
        public bool Start() => _process.Start();

        public void BeginOutputRead()
        {
            _process.BeginOutputReadLine();
            _process.BeginErrorReadLine();
        }

        public async Task SendCommandAsync(string command)
        {
            if (_process.HasExited)
                return;
            await _process.StandardInput.WriteLineAsync(command).ConfigureAwait(false);
            await _process.StandardInput.FlushAsync().ConfigureAwait(false);
        }

        public async Task<bool> WaitForExitAsync(TimeSpan timeout, CancellationToken cancellationToken = default)
        {
            using var timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutSource.CancelAfter(timeout);
            try
            {
                await _process.WaitForExitAsync(timeoutSource.Token).ConfigureAwait(false);
                return true;
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                return false;
            }
        }

        // Kill(true) requests tree termination, but WaitForExitAsync only waits for the root.
        public void KillTree() => _process.Kill(entireProcessTree: true);
        public void Dispose() => _process.Dispose();
    }
}
