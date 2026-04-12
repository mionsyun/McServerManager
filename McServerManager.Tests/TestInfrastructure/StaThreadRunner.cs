namespace McServerManager.Tests.TestInfrastructure;

internal static class StaThreadRunner
{
    public static void Run(Action action, TimeSpan? timeout = null)
    {
        Exception? exception = null;
        using var done = new ManualResetEventSlim(false);
        var waitTimeout = timeout ?? TimeSpan.FromSeconds(30);

        var thread = new Thread(() =>
        {
            try
            {
                action();
            }
            catch (Exception ex)
            {
                exception = ex;
            }
            finally
            {
                done.Set();
            }
        });

        thread.SetApartmentState(ApartmentState.STA);
        thread.IsBackground = true;
        thread.Start();
        if (!done.Wait(waitTimeout))
        {
            throw new TimeoutException($"STA test action did not complete within {waitTimeout.TotalSeconds:F0}s.");
        }

        if (exception is not null)
        {
            throw new AggregateException(exception);
        }
    }
}
