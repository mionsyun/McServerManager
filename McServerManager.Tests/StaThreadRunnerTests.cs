using System.Windows;
using McServerManager.Tests.TestInfrastructure;

namespace McServerManager.Tests;

public sealed class StaThreadRunnerTests
{
    [Fact]
    public void SequentialActionsUseTheSameLiveApplicationDispatcher()
    {
        var firstThread = 0;
        StaThreadRunner.Run(() =>
        {
            Assert.NotNull(Application.Current);
            Assert.True(Application.Current.Dispatcher.CheckAccess());
            firstThread = Environment.CurrentManagedThreadId;
        });
        StaThreadRunner.Run(() =>
        {
            Assert.Equal(firstThread, Environment.CurrentManagedThreadId);
            Assert.True(Application.Current.Dispatcher.CheckAccess());
            Assert.False(Application.Current.Dispatcher.HasShutdownStarted);
            Assert.True(Application.Current.Dispatcher.Invoke(() => true));
        });
    }
}
