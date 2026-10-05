using McServerManager.Services.RuntimeLifecycle;

namespace McServerManager.TemplateTests.RuntimeLifecycle;

public sealed class RuntimeOperationCoordinatorTests
{
    [Fact]
    public async Task LeaseExcludesOtherOperationsAndTryEnterNeverWaits()
    {
        var coordinator = new RuntimeOperationCoordinator();
        var first = await coordinator.EnterAsync();
        Assert.False(coordinator.TryEnter(out var rejected));
        Assert.Null(rejected);
        var pending = coordinator.EnterAsync().AsTask();
        Assert.False(pending.IsCompleted);
        first.Dispose();
        using var second = await pending.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.False(coordinator.TryEnter(out _));
    }

    [Fact]
    public async Task CancellationRemovesWaiterWithoutReleasingHeldLease()
    {
        var coordinator = new RuntimeOperationCoordinator();
        using var first = await coordinator.EnterAsync();
        using var source = new CancellationTokenSource();
        var pending = coordinator.EnterAsync(source.Token).AsTask();
        source.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => pending);
        Assert.False(coordinator.TryEnter(out _));
    }

    [Fact]
    public async Task DisposingLeaseTwiceDoesNotGrantTwoOperations()
    {
        var coordinator = new RuntimeOperationCoordinator();
        var first = await coordinator.EnterAsync();
        first.Dispose();
        first.Dispose();
        Assert.True(coordinator.TryEnter(out var second));
        using (second)
            Assert.False(coordinator.TryEnter(out _));
    }

    [Fact]
    public async Task ProductionCaptureProviderRejectsOrdinaryOperationLease()
    {
        using var operationLease = await RuntimeOperationCoordinator.Shared.EnterAsync();
        IStoppedRuntimeCaptureLeaseProvider provider = new UnsupportedStoppedRuntimeCaptureLeaseProvider();
        var error = await Assert.ThrowsAsync<NotSupportedException>(async () =>
            await provider.AcquireAsync("server", Path.GetTempPath()));
        Assert.Contains("Windows", error.Message);
        Assert.Contains("アプリ間", error.Message);
    }
}
