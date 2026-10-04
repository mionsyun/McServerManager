using McServerManager.Services;
using McServerManager.Services.RuntimeLifecycle;

namespace McServerManager.TemplateTests.RuntimeLifecycle;

public sealed class RuntimeResourceLimitTests
{
    [Fact]
    public void OutputQueueHasBoundedLinesAndRetainsReadinessBeyondLimit()
    {
        using var directory = new RuntimeTestDirectory();
        var runtime = new ServerRuntime(directory.CreateConfig(), action => action());
        using var process = new FakeServerProcess();
        var generation = runtime.AttachProcess(process, false);
        for (var i = 0; i < 1000; i++)
            runtime.TryQueueOutput(process, generation, new string('x', 17000), i == 999);

        var output = runtime.DrainOutput(process, generation, out var ready);
        Assert.True(ready);
        Assert.Equal(257, output.Length); // 256 retained lines and one omission notice.
        Assert.All(output.Take(256), line => Assert.Equal(16384, line.Length));
        Assert.Contains("744", output.Last());
        Assert.Empty(runtime.DrainOutput(process, generation, out var secondReady));
        Assert.False(secondReady);
    }

    [Fact]
    public void LogFloodQueuesOneNotificationAndAtMost512LinesPlusNotice()
    {
        using var directory = new RuntimeTestDirectory();
        var notifications = new Queue<Action>();
        var messages = new List<string>();
        var runtime = new ServerRuntime(directory.CreateConfig(), notifications.Enqueue);
        runtime.LogReceived += messages.Add;
        for (var i = 0; i < 1000; i++)
            runtime.AddLog(new string('x', 17000));

        Assert.Single(notifications);
        notifications.Dequeue()();
        Assert.Empty(notifications);
        Assert.Equal(512, messages.Count);
        Assert.All(messages, line => Assert.Equal(16384, line.Length));
        Assert.Equal(513, runtime.Logs.Count);
        Assert.Contains("488", runtime.Logs.Last());
    }

    [Theory]
    [InlineData(-1, 1)]
    [InlineData(0, 1)]
    [InlineData(5000, 5000)]
    [InlineData(int.MaxValue, 5000)]
    public void DisplayRetentionIsClamped(int requested, int expected)
    {
        using var directory = new RuntimeTestDirectory();
        var runtime = new ServerRuntime(directory.CreateConfig(), action => action());
        for (var i = 0; i < 5100; i++) runtime.AddLog(i.ToString(), requested);
        Assert.Equal(expected, runtime.Logs.Count);
        Assert.EndsWith("5099", runtime.Logs.Last());
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(301)]
    public async Task TerminatorRejectsOutOfRangeTimeoutWithoutSendingCommands(int seconds)
    {
        using var process = new FakeServerProcess();
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() =>
            new ProcessTreeTerminator().StopAsync(process, TimeSpan.FromSeconds(seconds)));
        Assert.Empty(process.Commands);
        Assert.Equal(0, process.KillCalls);
        Assert.Equal(0, process.WaitCalls);
    }
    [Fact]
    public async Task CollectionNotificationsDoNotHoldRuntimeStateLock()
    {
        using var directory = new RuntimeTestDirectory();
        var config = directory.CreateConfig();
        var runtime = new ServerRuntime(config, action => action());
        using var stateUpdated = new ManualResetEventSlim();
        Task? updating = null;
        var observedUpdate = false;
        runtime.Logs.CollectionChanged += (_, _) =>
        {
            updating = Task.Run(() =>
            {
                runtime.UpdateConfig(config);
                stateUpdated.Set();
            });
            // This callback is on a synthetic worker, not a UI thread. A bounded wait
            // exposes a held state lock without leaving a deadlocked test behind.
            observedUpdate = stateUpdated.Wait(TimeSpan.FromSeconds(2));
        };

        await Task.Run(() => runtime.AddLog("synthetic log")).WaitAsync(TimeSpan.FromSeconds(5));
        Assert.NotNull(updating);
        await updating!.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.True(observedUpdate);
    }

}
