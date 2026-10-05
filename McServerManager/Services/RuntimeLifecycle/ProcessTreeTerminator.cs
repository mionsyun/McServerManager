using System.Threading;
using McServerManager.Models.RuntimeLifecycle;

namespace McServerManager.Services.RuntimeLifecycle;

/// <summary>Best-effort ordinary server shutdown. This adapter cannot certify native tree ownership.</summary>
public sealed class ProcessTreeTerminator : IProcessTreeTerminator
{
    public async Task<ProcessStopResult> StopAsync(
        IServerProcess process, TimeSpan timeout, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(process);
        if (timeout < TimeSpan.Zero || timeout > TimeSpan.FromMinutes(5))
            throw new ArgumentOutOfRangeException(nameof(timeout));

        var forced = false;
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            // A broken/full stdin must not prevent bounded wait/termination attempts.
            try
            {
                await process.SendCommandAsync("stop").WaitAsync(timeout, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception) when (!cancellationToken.IsCancellationRequested)
            {
                // The authoritative outcome comes from waiting on the owned root below.
            }

            if (await process.WaitForExitAsync(timeout, cancellationToken).ConfigureAwait(false))
                return new ProcessStopResult(ProcessStopOutcome.RootExitedUnverified, false);

            cancellationToken.ThrowIfCancellationRequested();
            forced = true;
            process.KillTree();
            var forceWait = timeout < TimeSpan.FromSeconds(1) ? TimeSpan.FromSeconds(1) : timeout;
            if (await process.WaitForExitAsync(forceWait, cancellationToken).ConfigureAwait(false))
                return new ProcessStopResult(ProcessStopOutcome.RootExitedUnverified, true);

            return new ProcessStopResult(ProcessStopOutcome.Uncertain, true, "強制終了後も起動プロセスの終了を確認できませんでした。");
        }
        catch (Exception ex)
        {
            return new ProcessStopResult(ProcessStopOutcome.Uncertain, forced, ex.Message);
        }
    }
}
