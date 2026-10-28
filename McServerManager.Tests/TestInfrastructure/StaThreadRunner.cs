using System.Windows;
using System.Windows.Threading;

namespace McServerManager.Tests.TestInfrastructure;

internal static class StaThreadRunner
{
    // WPF permits one Application per AppDomain. Its dispatcher must outlive each
    // individual test, otherwise the next test can invoke a dead STA thread.
    private static readonly Lazy<Dispatcher> SharedDispatcher = new(CreateDispatcher);

    public static void Run(Action action, TimeSpan? timeout = null)
    {
        ArgumentNullException.ThrowIfNull(action);
        var waitTimeout = timeout ?? TimeSpan.FromSeconds(30);
        var dispatcher = SharedDispatcher.Value;
        if (dispatcher.CheckAccess())
        {
            action();
            return;
        }
        dispatcher.InvokeAsync(action).Task.WaitAsync(waitTimeout).GetAwaiter().GetResult();
    }

    private static Dispatcher CreateDispatcher()
    {
        var ready = new TaskCompletionSource<Dispatcher>(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            try
            {
                var dispatcher = Dispatcher.CurrentDispatcher;
                SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(dispatcher));
                _ = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
                ready.SetResult(dispatcher);
                Dispatcher.Run();
            }
            catch (Exception ex)
            {
                ready.TrySetException(ex);
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.IsBackground = true;
        thread.Name = "MaiPilot test dispatcher";
        thread.Start();
        return ready.Task.WaitAsync(TimeSpan.FromSeconds(30)).GetAwaiter().GetResult();
    }
}
