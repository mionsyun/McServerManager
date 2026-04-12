namespace McServerManager.Tests.TestInfrastructure;

internal static class AsyncTestWait
{
    public static async Task WaitUntilAsync(Func<bool> predicate, TimeSpan timeout, TimeSpan? pollInterval = null)
    {
        var interval = pollInterval ?? TimeSpan.FromMilliseconds(50);
        var deadline = DateTime.UtcNow + timeout;

        while (DateTime.UtcNow < deadline)
        {
            if (predicate())
            {
                return;
            }

            await Task.Delay(interval).ConfigureAwait(false);
        }

        throw new TimeoutException($"Condition was not met within {timeout.TotalMilliseconds}ms.");
    }
}
