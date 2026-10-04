using McServerManager.Models;
using McServerManager.Models.RuntimeLifecycle;
using McServerManager.Services;
using McServerManager.Services.RuntimeLifecycle;

namespace McServerManager.TemplateTests.RuntimeLifecycle;

public sealed class ServerRuntimeManagerTests
{
    [Theory]
    [InlineData("kill")]
    [InlineData("wait")]
    [InlineData("wait-exception")]
    public async Task FailedTerminationLeavesActualManagerUnknownAndRetainsOwnedHandle(string failure)
    {
        using var directory = new RuntimeTestDirectory();
        var config = directory.CreateConfig();
        var process = new FakeServerProcess
        {
            WaitBehavior = failure == "wait-exception"
                ? (_, _) => Task.FromException<bool>(new IOException("Synthetic wait failure"))
                : (_, _) => Task.FromResult(false),
            KillBehavior = failure == "kill"
                ? () => throw new InvalidOperationException("Synthetic kill failure")
                : null
        };
        var factory = new FakeServerProcessFactory();
        factory.Enqueue(process);
        var manager = CreateManager(factory, new ProcessTreeTerminator());
        var runtime = manager.GetOrCreate(config);

        await manager.StartAsync(config, directory.Root).WaitAsync(RuntimeTestDirectory.Timeout);
        await manager.StopAsync(config, timeoutSeconds: 0).WaitAsync(RuntimeTestDirectory.Timeout);

        Assert.Equal(ServerStatus.Unknown, runtime.Status);
        Assert.Equal(RuntimeLifetimeState.OwnedStopUncertain, runtime.Lifetime.State);
        Assert.Equal(ProcessStopOutcome.Uncertain, runtime.LastStopResult?.Outcome);
        Assert.False(manager.TryRelease(config.ServerId));
        Assert.Same(runtime, manager.GetOrCreate(config));
        Assert.Equal(0, process.DisposeCalls);
        Assert.Contains("stop", process.Commands);
        Assert.True(process.WaitCalls >= 1);
        await manager.StartAsync(config, directory.Root).WaitAsync(RuntimeTestDirectory.Timeout);
        Assert.Equal(1, factory.CreateCalls);
    }

    [Theory]
    [InlineData(false, ServerStatus.Stopped)]
    [InlineData(true, ServerStatus.Unknown)]
    public async Task UnexpectedRootExitDistinguishesDirectLaunchFromShellWrapper(
        bool shellWrapper, ServerStatus expectedStatus)
    {
        using var directory = new RuntimeTestDirectory();
        var config = directory.CreateConfig();
        if (shellWrapper)
        {
            directory.AddShellWrapper();
            config.Type = "Forge";
            config.AutoRestartOnCrash = true;
        }
        var process = new FakeServerProcess();
        var factory = new FakeServerProcessFactory();
        factory.Enqueue(process);
        var delay = new ManualRuntimeDelay();
        var manager = CreateManager(factory, delay: delay);
        var runtime = manager.GetOrCreate(config);
        await manager.StartAsync(config, directory.Root).WaitAsync(RuntimeTestDirectory.Timeout);

        await WaitForStatusAsync(runtime, expectedStatus, () => process.EmitExit(7));

        await DrainOperationsAsync(manager);
        Assert.Equal(expectedStatus, runtime.Status);
        Assert.Equal(0, delay.Calls);
        Assert.Equal(shellWrapper ? "cmd.exe" : "java", Assert.Single(factory.StartInfos).FileName);
        if (shellWrapper)
        {
            Assert.False(manager.TryRelease(config.ServerId));
            await manager.StartAsync(config, directory.Root).WaitAsync(RuntimeTestDirectory.Timeout);
            Assert.Equal(1, factory.CreateCalls);
        }
    }

    [Theory]
    [InlineData(false, ServerStatus.Stopped)]
    [InlineData(true, ServerStatus.Unknown)]
    public async Task RequestedRootStopDoesNotCertifyShellDescendantsStopped(
        bool shellWrapper, ServerStatus expectedStatus)
    {
        using var directory = new RuntimeTestDirectory();
        var config = directory.CreateConfig();
        if (shellWrapper)
        {
            directory.AddShellWrapper();
            config.Type = "Forge";
        }
        var factory = new FakeServerProcessFactory();
        var terminator = new FakeProcessTreeTerminator();
        var manager = CreateManager(factory, terminator);
        var runtime = manager.GetOrCreate(config);
        var crashes = 0;
        manager.ServerCrashed += (_, _) => Interlocked.Increment(ref crashes);
        await manager.StartAsync(config, directory.Root).WaitAsync(RuntimeTestDirectory.Timeout);

        await manager.StopAsync(config).WaitAsync(RuntimeTestDirectory.Timeout);

        Assert.Equal(expectedStatus, runtime.Status);
        Assert.Equal(1, terminator.StopCalls);
        Assert.Equal(0, crashes);
        if (shellWrapper)
            Assert.False(manager.TryRelease(config.ServerId));
    }

    [Fact]
    public async Task RestartDoesNotLaunchReplacementWhenStopIsUncertain()
    {
        using var directory = new RuntimeTestDirectory();
        var config = directory.CreateConfig();
        var factory = new FakeServerProcessFactory();
        var terminator = new FakeProcessTreeTerminator
        {
            StopBehavior = (_, _, _) => Task.FromResult(
                new ProcessStopResult(ProcessStopOutcome.Uncertain, true, "Synthetic stop uncertainty"))
        };
        var manager = CreateManager(factory, terminator);
        var runtime = manager.GetOrCreate(config);
        await manager.StartAsync(config, directory.Root).WaitAsync(RuntimeTestDirectory.Timeout);

        await Assert.ThrowsAsync<InvalidOperationException>(() => manager.RestartAsync(config, directory.Root).WaitAsync(RuntimeTestDirectory.Timeout));

        Assert.Equal(ServerStatus.Unknown, runtime.Status);
        Assert.Equal(1, factory.CreateCalls);
        Assert.Equal(1, terminator.StopCalls);
        Assert.Equal(0, Assert.Single(factory.Created).DisposeCalls);
    }

    [Fact]
    public async Task ConcurrentStartsHaveOnlyOneActualLaunch()
    {
        using var directory = new RuntimeTestDirectory();
        var config = directory.CreateConfig();
        using var releaseStart = new ManualResetEventSlim();
        var process = new FakeServerProcess
        {
            StartBehavior = () =>
            {
                // The process interface has a synchronous Start. Only the Task.Run test worker waits here.
                if (!releaseStart.Wait(RuntimeTestDirectory.Timeout))
                    throw new TimeoutException("The test did not release the synthetic launch.");
                return true;
            }
        };
        var factory = new FakeServerProcessFactory();
        factory.Enqueue(process);
        var operations = new ObservedRuntimeOperationCoordinator();
        var manager = CreateManager(factory, operations: operations);
        var first = Task.Run(() => manager.StartAsync(config, directory.Root));
        try
        {
            await process.StartEntered.Task.WaitAsync(RuntimeTestDirectory.Timeout);
            var following = Enumerable.Range(0, 16)
                .Select(_ => manager.StartAsync(config, directory.Root)).ToArray();
            await operations.WaitForEnterCallsAsync(17);
            Assert.All(following, task => Assert.False(task.IsCompleted));
            Assert.Equal(1, factory.CreateCalls);
            releaseStart.Set();
            await Task.WhenAll(following.Append(first)).WaitAsync(RuntimeTestDirectory.Timeout);
        }
        finally
        {
            releaseStart.Set();
        }

        Assert.Equal(1, factory.CreateCalls);
        Assert.Equal(1, process.StartCalls);
        Assert.Equal(ServerStatus.Starting, manager.GetOrCreate(config).Status);
    }

    [Fact]
    public async Task StartStopAndRestartAreSerializedAroundTheActualProcessLaunch()
    {
        using var directory = new RuntimeTestDirectory();
        var config = directory.CreateConfig();
        using var releaseStart = new ManualResetEventSlim();
        var process = new FakeServerProcess
        {
            StartBehavior = () =>
            {
                // The process interface has a synchronous Start. Only the Task.Run test worker waits here.
                if (!releaseStart.Wait(RuntimeTestDirectory.Timeout))
                    throw new TimeoutException("The test did not release the synthetic launch.");
                return true;
            }
        };
        var factory = new FakeServerProcessFactory();
        factory.Enqueue(process);
        var terminator = new FakeProcessTreeTerminator();
        var operations = new ObservedRuntimeOperationCoordinator();
        var manager = CreateManager(factory, terminator, operations);
        var start = Task.Run(() => manager.StartAsync(config, directory.Root));
        try
        {
            await process.StartEntered.Task.WaitAsync(RuntimeTestDirectory.Timeout);
            var stop = manager.StopAsync(config);
            await operations.WaitForEnterCallsAsync(2);
            var restart = manager.RestartAsync(config, directory.Root);
            await operations.WaitForEnterCallsAsync(3);
            Assert.False(stop.IsCompleted);
            Assert.False(restart.IsCompleted);
            Assert.Equal(0, terminator.StopCalls);
            Assert.Equal(1, factory.CreateCalls);
            releaseStart.Set();
            await Task.WhenAll(start, stop, restart).WaitAsync(RuntimeTestDirectory.Timeout);
        }
        finally
        {
            releaseStart.Set();
        }

        Assert.Equal(2, factory.CreateCalls);
        Assert.Equal(1, terminator.StopCalls);
        Assert.Same(process, Assert.Single(terminator.Targets));
        Assert.Equal(ServerStatus.Starting, manager.GetOrCreate(config).Status);
    }

    [Fact]
    public async Task RestartKeepsTheOperationLeaseUntilReplacementIsStarted()
    {
        using var directory = new RuntimeTestDirectory();
        var config = directory.CreateConfig();
        var releaseStop = new TaskCompletionSource<ProcessStopResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        var terminator = new FakeProcessTreeTerminator { StopBehavior = (_, _, _) => releaseStop.Task };
        var factory = new FakeServerProcessFactory();
        var operations = new ObservedRuntimeOperationCoordinator();
        var manager = CreateManager(factory, terminator, operations);
        await manager.StartAsync(config, directory.Root).WaitAsync(RuntimeTestDirectory.Timeout);

        var restart = manager.RestartAsync(config, directory.Root);
        await terminator.StopEntered.Task.WaitAsync(RuntimeTestDirectory.Timeout);
        var competingStart = manager.StartAsync(config, directory.Root);
        try
        {
            await operations.WaitForEnterCallsAsync(3);
            Assert.False(restart.IsCompleted);
            Assert.False(competingStart.IsCompleted);
            Assert.False(operations.TryEnter(out _));
            Assert.Equal(1, factory.CreateCalls);
        }
        finally
        {
            releaseStop.TrySetResult(new ProcessStopResult(ProcessStopOutcome.RootExitedUnverified, false));
        }
        await Task.WhenAll(restart, competingStart).WaitAsync(RuntimeTestDirectory.Timeout);

        Assert.Equal(2, factory.CreateCalls);
        Assert.Equal(1, terminator.StopCalls);
        Assert.Equal(ServerStatus.Starting, manager.GetOrCreate(config).Status);
    }

    [Fact]
    public async Task OldProcessCallbacksCannotChangeNewGenerationOrAppendItsOutput()
    {
        using var directory = new RuntimeTestDirectory();
        var config = directory.CreateConfig();
        var oldProcess = new FakeServerProcess();
        var factory = new FakeServerProcessFactory();
        factory.Enqueue(oldProcess);
        var manager = CreateManager(factory);
        var runtime = manager.GetOrCreate(config);
        var crashes = 0;
        manager.ServerCrashed += (_, _) => Interlocked.Increment(ref crashes);
        await manager.StartAsync(config, directory.Root).WaitAsync(RuntimeTestDirectory.Timeout);
        var stale = oldProcess.CaptureCallbacks();
        Assert.NotNull(stale.Output);
        Assert.NotNull(stale.Exit);
        await manager.RestartAsync(config, directory.Root).WaitAsync(RuntimeTestDirectory.Timeout);
        var logsBefore = runtime.Logs.Count;

        stale.Output!("stale-generation-output Done (0.001s)!");
        stale.Exit!();
        await DrainOperationsAsync(manager);

        Assert.Equal(ServerStatus.Starting, runtime.Status);
        Assert.Equal(logsBefore, runtime.Logs.Count);
        Assert.DoesNotContain(runtime.Logs, line => line.Contains("stale-generation-output", StringComparison.Ordinal));
        Assert.Equal(0, crashes);
        Assert.Equal(2, factory.CreateCalls);
    }

    [Fact]
    public async Task OperationLeaseBlocksManualStartUntilReleased()
    {
        using var directory = new RuntimeTestDirectory();
        var config = directory.CreateConfig();
        var factory = new FakeServerProcessFactory();
        var operations = new ObservedRuntimeOperationCoordinator();
        var manager = CreateManager(factory, operations: operations);
        var lease = await operations.HoldAsync();
        Task start;
        try
        {
            start = manager.StartAsync(config, directory.Root);
            await operations.WaitForEnterCallsAsync(1);
            Assert.False(start.IsCompleted);
            Assert.Equal(0, factory.CreateCalls);
        }
        finally
        {
            lease.Dispose();
        }
        await start.WaitAsync(RuntimeTestDirectory.Timeout);

        Assert.Equal(1, factory.CreateCalls);
        Assert.Equal(ServerStatus.Starting, manager.GetOrCreate(config).Status);
    }

    [Fact]
    public async Task TryReleaseReturnsWithoutWaitingForHeldOperationLease()
    {
        using var directory = new RuntimeTestDirectory();
        var config = directory.CreateConfig();
        var operations = new ObservedRuntimeOperationCoordinator();
        var manager = CreateManager(new FakeServerProcessFactory(), operations: operations);
        var runtime = manager.GetOrCreate(config);
        var lease = await operations.HoldAsync();
        try
        {
            var released = await Task.Run(() => manager.TryRelease(config.ServerId))
                .WaitAsync(RuntimeTestDirectory.Timeout);
            Assert.False(released);
            Assert.Same(runtime, manager.GetOrCreate(config));
        }
        finally
        {
            lease.Dispose();
        }

        Assert.True(manager.TryRelease(config.ServerId));
        Assert.NotSame(runtime, manager.GetOrCreate(config));
    }

    [Fact]
    public async Task StartRejectsDirectoryDifferentFromConfigWithoutCreatingProcess()
    {
        using var directory = new RuntimeTestDirectory();
        using var otherDirectory = new RuntimeTestDirectory();
        var config = directory.CreateConfig();
        var factory = new FakeServerProcessFactory();
        var manager = CreateManager(factory);

        await Assert.ThrowsAsync<InvalidOperationException>(() => manager.StartAsync(config, otherDirectory.Root).WaitAsync(RuntimeTestDirectory.Timeout));

        Assert.Equal(0, factory.CreateCalls);
    }

    [Fact]
    public async Task ExistingServerIdCannotBeReboundToAnotherDirectory()
    {
        using var directory = new RuntimeTestDirectory();
        using var otherDirectory = new RuntimeTestDirectory();
        var config = directory.CreateConfig();
        var alternate = otherDirectory.CreateConfig(config.ServerId);
        var factory = new FakeServerProcessFactory();
        var manager = CreateManager(factory);
        var runtime = manager.GetOrCreate(config);
        await manager.StartAsync(config, directory.Root).WaitAsync(RuntimeTestDirectory.Timeout);

        Assert.Throws<InvalidOperationException>(() => manager.GetOrCreate(alternate));
        await Assert.ThrowsAsync<InvalidOperationException>(() => manager.StartAsync(alternate, otherDirectory.Root).WaitAsync(RuntimeTestDirectory.Timeout));

        Assert.Same(runtime, manager.GetOrCreate(config));
        Assert.Equal(ServerStatus.Starting, runtime.Status);
        Assert.Equal(1, factory.CreateCalls);
    }

    [Fact]
    public async Task LateExitAfterFailedRequestedStopDoesNotBecomeCrashOrAutoRestart()
    {
        using var directory = new RuntimeTestDirectory();
        var config = directory.CreateConfig();
        config.AutoRestartOnCrash = true;
        var process = new FakeServerProcess();
        var factory = new FakeServerProcessFactory();
        factory.Enqueue(process);
        var delay = new ManualRuntimeDelay();
        var terminator = new FakeProcessTreeTerminator
        {
            StopBehavior = (_, _, _) => Task.FromResult(new ProcessStopResult(ProcessStopOutcome.Uncertain, true))
        };
        var manager = CreateManager(factory, terminator, delay: delay);
        var runtime = manager.GetOrCreate(config);
        var crashes = 0;
        manager.ServerCrashed += (_, _) => Interlocked.Increment(ref crashes);
        await manager.StartAsync(config, directory.Root).WaitAsync(RuntimeTestDirectory.Timeout);
        await manager.StopAsync(config).WaitAsync(RuntimeTestDirectory.Timeout);
        Assert.Equal(ServerStatus.Unknown, runtime.Status);

        process.EmitExit(137);
        await DrainOperationsAsync(manager);

        Assert.Equal(ServerStatus.Unknown, runtime.Status);
        Assert.Equal(RuntimeLifetimeState.OwnedStopUncertain, runtime.Lifetime.State);
        Assert.Equal(0, crashes);
        Assert.Equal(0, delay.Calls);
        Assert.Equal(1, factory.CreateCalls);
        Assert.False(manager.TryRelease(config.ServerId));
    }

    [Fact]
    public async Task ManualStopCancelsPendingRestartEvenWhenTimerDeliveryLosesCancellationRace()
    {
        using var directory = new RuntimeTestDirectory();
        var config = directory.CreateConfig();
        config.AutoRestartOnCrash = true;
        var process = new FakeServerProcess();
        var factory = new FakeServerProcessFactory();
        factory.Enqueue(process);
        var delay = new ManualRuntimeDelay { IgnoreCancellation = true };
        var operations = new ObservedRuntimeOperationCoordinator();
        var manager = CreateManager(factory, operations: operations, delay: delay);
        await manager.StartAsync(config, directory.Root).WaitAsync(RuntimeTestDirectory.Timeout);
        process.EmitExit(1);
        var request = await delay.Scheduled.Task.WaitAsync(RuntimeTestDirectory.Timeout);
        Assert.Equal(TimeSpan.FromSeconds(1), request.Duration);

        await manager.StopAsync(config).WaitAsync(RuntimeTestDirectory.Timeout);
        await request.CancellationObserved.Task.WaitAsync(RuntimeTestDirectory.Timeout);
        var nextEnter = operations.EnterCalls + 1;
        request.Release.TrySetResult();
        await operations.WaitForEnterCallsAsync(nextEnter);
        await DrainOperationsAsync(manager);

        Assert.True(request.Token.IsCancellationRequested);
        Assert.Equal(ServerStatus.Stopped, manager.GetOrCreate(config).Status);
        Assert.Equal(1, factory.CreateCalls);
        Assert.Equal(1, delay.Calls);
    }

    [Fact]
    public async Task ManualStartSupersedesPendingAutoRestartWithoutAnExtraLaunch()
    {
        using var directory = new RuntimeTestDirectory();
        var config = directory.CreateConfig();
        config.AutoRestartOnCrash = true;
        var process = new FakeServerProcess();
        var factory = new FakeServerProcessFactory();
        factory.Enqueue(process);
        var delay = new ManualRuntimeDelay { IgnoreCancellation = true };
        var operations = new ObservedRuntimeOperationCoordinator();
        var manager = CreateManager(factory, operations: operations, delay: delay);
        var runtime = manager.GetOrCreate(config);
        await manager.StartAsync(config, directory.Root).WaitAsync(RuntimeTestDirectory.Timeout);
        process.EmitExit(1);
        var request = await delay.Scheduled.Task.WaitAsync(RuntimeTestDirectory.Timeout);

        await manager.StartAsync(config, directory.Root).WaitAsync(RuntimeTestDirectory.Timeout);
        var newGeneration = runtime.Lifetime.Generation;
        var nextEnter = operations.EnterCalls + 1;
        request.Release.TrySetResult();
        await operations.WaitForEnterCallsAsync(nextEnter);
        await DrainOperationsAsync(manager);

        Assert.True(request.Token.IsCancellationRequested);
        Assert.Equal(2, factory.CreateCalls);
        Assert.Equal(newGeneration, runtime.Lifetime.Generation);
        Assert.Equal(ServerStatus.Starting, runtime.Status);
    }

    [Fact]
    public async Task OperationLeaseBlocksAutoRestartAfterItsTimerHasCompleted()
    {
        using var directory = new RuntimeTestDirectory();
        var config = directory.CreateConfig();
        config.AutoRestartOnCrash = true;
        var first = new FakeServerProcess();
        var replacement = new FakeServerProcess();
        var factory = new FakeServerProcessFactory();
        factory.Enqueue(first);
        factory.Enqueue(replacement);
        var delay = new ManualRuntimeDelay();
        var operations = new ObservedRuntimeOperationCoordinator();
        var manager = CreateManager(factory, operations: operations, delay: delay);
        await manager.StartAsync(config, directory.Root).WaitAsync(RuntimeTestDirectory.Timeout);
        first.EmitExit(1);
        var request = await delay.Scheduled.Task.WaitAsync(RuntimeTestDirectory.Timeout);
        var lease = await operations.HoldAsync();
        try
        {
            var nextEnter = operations.EnterCalls + 1;
            request.Release.TrySetResult();
            await operations.WaitForEnterCallsAsync(nextEnter);
            Assert.Equal(1, factory.CreateCalls);
            Assert.False(replacement.StartEntered.Task.IsCompleted);
            Assert.Equal(ServerStatus.Stopped, manager.GetOrCreate(config).Status);
        }
        finally
        {
            lease.Dispose();
        }
        await replacement.StartEntered.Task.WaitAsync(RuntimeTestDirectory.Timeout);
        await DrainOperationsAsync(manager);

        Assert.Equal(2, factory.CreateCalls);
        Assert.Equal(RuntimeLifetimeState.OwnedRunning, manager.GetOrCreate(config).Lifetime.State);
    }

    [Fact]
    public async Task ManualStopCancelsAutoRestartAlreadyQueuedBehindOperationLease()
    {
        using var directory = new RuntimeTestDirectory();
        var config = directory.CreateConfig();
        config.AutoRestartOnCrash = true;
        var process = new FakeServerProcess();
        var factory = new FakeServerProcessFactory();
        factory.Enqueue(process);
        var delay = new ManualRuntimeDelay();
        var operations = new ObservedRuntimeOperationCoordinator();
        var manager = CreateManager(factory, operations: operations, delay: delay);
        await manager.StartAsync(config, directory.Root).WaitAsync(RuntimeTestDirectory.Timeout);
        process.EmitExit(1);
        var request = await delay.Scheduled.Task.WaitAsync(RuntimeTestDirectory.Timeout);
        var lease = await operations.HoldAsync();
        Task stop;
        try
        {
            var nextEnter = operations.EnterCalls + 1;
            request.Release.TrySetResult();
            await operations.WaitForEnterCallsAsync(nextEnter);
            stop = manager.StopAsync(config);
            await operations.WaitForEnterCallsAsync(nextEnter + 1);
            Assert.False(stop.IsCompleted);
            Assert.True(request.Token.IsCancellationRequested);
            Assert.Equal(1, factory.CreateCalls);
        }
        finally
        {
            lease.Dispose();
        }
        await stop.WaitAsync(RuntimeTestDirectory.Timeout);
        await DrainOperationsAsync(manager);

        Assert.Equal(1, factory.CreateCalls);
        Assert.Equal(ServerStatus.Stopped, manager.GetOrCreate(config).Status);
    }

    [Fact]
    public async Task DuplicateExitCallbacksScheduleOnlyOneCrashRestart()
    {
        using var directory = new RuntimeTestDirectory();
        var config = directory.CreateConfig();
        config.AutoRestartOnCrash = true;
        var process = new FakeServerProcess();
        var factory = new FakeServerProcessFactory();
        factory.Enqueue(process);
        var delay = new ManualRuntimeDelay();
        var manager = CreateManager(factory, delay: delay);
        var crashes = 0;
        manager.ServerCrashed += (_, _) => Interlocked.Increment(ref crashes);
        await manager.StartAsync(config, directory.Root).WaitAsync(RuntimeTestDirectory.Timeout);
        process.EmitExit(1);
        var request = await delay.Scheduled.Task.WaitAsync(RuntimeTestDirectory.Timeout);
        process.EmitExit(1);
        await DrainOperationsAsync(manager);

        Assert.Equal(1, crashes);
        Assert.Equal(1, delay.Calls);
        Assert.Equal(1, factory.CreateCalls);
        await manager.StopAsync(config).WaitAsync(RuntimeTestDirectory.Timeout);
        await request.CancellationObserved.Task.WaitAsync(RuntimeTestDirectory.Timeout);
    }

    [Fact]
    public async Task CurrentOutputMarksStartupCompleteWithoutGrantingStoppedTreeProof()
    {
        using var directory = new RuntimeTestDirectory();
        var config = directory.CreateConfig();
        var process = new FakeServerProcess();
        var factory = new FakeServerProcessFactory();
        factory.Enqueue(process);
        var manager = CreateManager(factory);
        var runtime = manager.GetOrCreate(config);
        await manager.StartAsync(config, directory.Root).WaitAsync(RuntimeTestDirectory.Timeout);
        Assert.Equal(ServerStatus.Starting, runtime.Status);

        await WaitForStatusAsync(runtime, ServerStatus.Running,
            () => process.EmitOutput("Done (0.001s)! For help, type help"));
        await DrainOperationsAsync(manager);

        Assert.Equal(ServerStatus.Running, runtime.Status);
        Assert.Equal(RuntimeLifetimeState.OwnedRunning, runtime.Lifetime.State);
        Assert.Null(runtime.Process);
        Assert.Contains(runtime.Logs, line => line.Contains("Done (", StringComparison.Ordinal));
    }

    [Fact]
    public async Task MutatingRegisteredConfigurationDoesNotRedirectStopOrLaunch()
    {
        using var directory = new RuntimeTestDirectory();
        using var otherDirectory = new RuntimeTestDirectory();
        var config = directory.CreateConfig();
        var factory = new FakeServerProcessFactory();
        var terminator = new FakeProcessTreeTerminator();
        var manager = CreateManager(factory, terminator);
        var runtime = manager.GetOrCreate(config);
        await manager.StartAsync(config, directory.Root).WaitAsync(RuntimeTestDirectory.Timeout);
        config.DirectoryPath = otherDirectory.Root;

        await Assert.ThrowsAsync<InvalidOperationException>(() => manager.StopAsync(config).WaitAsync(RuntimeTestDirectory.Timeout));
        await Assert.ThrowsAsync<InvalidOperationException>(() => manager.StartAsync(config, otherDirectory.Root).WaitAsync(RuntimeTestDirectory.Timeout));

        Assert.Equal(directory.Root, runtime.RegisteredDirectory);
        Assert.Equal(0, terminator.StopCalls);
        Assert.Equal(1, factory.CreateCalls);
        Assert.Equal(RuntimeLifetimeState.OwnedRunning, runtime.Lifetime.State);
    }

    [Fact]
    public async Task SameDirectoryUnderAnotherServerIdCannotLaunchOverOwnedProcess()
    {
        using var directory = new RuntimeTestDirectory();
        var config = directory.CreateConfig();
        var alias = directory.CreateConfig("same-directory-alias");
        var factory = new FakeServerProcessFactory();
        var manager = CreateManager(factory);
        await manager.StartAsync(config, directory.Root).WaitAsync(RuntimeTestDirectory.Timeout);

        await Assert.ThrowsAsync<InvalidOperationException>(() => manager.StartAsync(alias, directory.Root).WaitAsync(RuntimeTestDirectory.Timeout));

        Assert.Equal(1, factory.CreateCalls);
        Assert.Equal(RuntimeLifetimeState.OwnedRunning, manager.GetOrCreate(config).Lifetime.State);
    }

    [Fact]
    public async Task StartupMarkerSurvivesDisplayOutputFloodWhileOperationLeaseIsHeld()
    {
        using var directory = new RuntimeTestDirectory();
        var config = directory.CreateConfig();
        var process = new FakeServerProcess();
        var factory = new FakeServerProcessFactory();
        factory.Enqueue(process);
        var operations = new ObservedRuntimeOperationCoordinator();
        var manager = CreateManager(factory, operations: operations);
        var runtime = manager.GetOrCreate(config);
        await manager.StartAsync(config, directory.Root).WaitAsync(RuntimeTestDirectory.Timeout);
        var lease = await operations.HoldAsync();
        try
        {
            for (var index = 0; index < 1024; index++)
                process.EmitOutput($"Synthetic flood output {index}");
            process.EmitOutput("Done (0.001s)! For help, type help");
            Assert.Equal(ServerStatus.Starting, runtime.Status);
        }
        finally
        {
            lease.Dispose();
        }
        await DrainOperationsAsync(manager);

        Assert.Equal(ServerStatus.Running, runtime.Status);
        Assert.Equal(RuntimeLifetimeState.OwnedRunning, runtime.Lifetime.State);
        Assert.True(runtime.Logs.Count < 1024);
    }

    [Fact]
    public async Task FaultingStatusAndLogSubscribersDoNotHideUnknownFromLaterSubscribers()
    {
        using var directory = new RuntimeTestDirectory();
        var config = directory.CreateConfig();
        var factory = new FakeServerProcessFactory();
        var terminator = new FakeProcessTreeTerminator
        {
            StopBehavior = (_, _, _) => Task.FromResult(new ProcessStopResult(ProcessStopOutcome.Uncertain, true))
        };
        var manager = CreateManager(factory, terminator);
        var runtime = manager.GetOrCreate(config);
        var statuses = new List<ServerStatus>();
        var logs = new List<string>();
        runtime.StatusChanged += _ => throw new InvalidOperationException("Synthetic first status subscriber failure");
        runtime.StatusChanged += statuses.Add;
        runtime.LogReceived += _ => throw new InvalidOperationException("Synthetic first log subscriber failure");
        runtime.LogReceived += logs.Add;

        await manager.StartAsync(config, directory.Root).WaitAsync(RuntimeTestDirectory.Timeout);
        await manager.StopAsync(config).WaitAsync(RuntimeTestDirectory.Timeout);

        Assert.Equal(ServerStatus.Unknown, runtime.Status);
        Assert.Contains(ServerStatus.Unknown, statuses);
        Assert.NotEmpty(logs);
        Assert.False(manager.TryRelease(config.ServerId));
    }

    [Fact]
    public async Task FaultingCrashSubscriberDoesNotSuppressOtherListenersOrPendingAutoRestart()
    {
        using var directory = new RuntimeTestDirectory();
        var config = directory.CreateConfig();
        config.AutoRestartOnCrash = true;
        var process = new FakeServerProcess();
        var factory = new FakeServerProcessFactory();
        factory.Enqueue(process);
        var delay = new ManualRuntimeDelay();
        var manager = CreateManager(factory, delay: delay);
        var crashes = 0;
        manager.ServerCrashed += (_, _) => throw new InvalidOperationException("Synthetic first crash subscriber failure");
        manager.ServerCrashed += (_, _) => Interlocked.Increment(ref crashes);
        await manager.StartAsync(config, directory.Root).WaitAsync(RuntimeTestDirectory.Timeout);

        process.EmitExit(7);
        var request = await delay.Scheduled.Task.WaitAsync(RuntimeTestDirectory.Timeout);
        await DrainOperationsAsync(manager);

        Assert.Equal(1, crashes);
        Assert.Equal(1, delay.Calls);
        Assert.Equal(ServerStatus.Stopped, manager.GetOrCreate(config).Status);
        await manager.StopAsync(config).WaitAsync(RuntimeTestDirectory.Timeout);
        await request.CancellationObserved.Task.WaitAsync(RuntimeTestDirectory.Timeout);
    }

    [Fact]
    public async Task QueuedNotificationsReadCurrentStatusAndIgnoreOldGenerationCrash()
    {
        using var directory = new RuntimeTestDirectory();
        var config = directory.CreateConfig();
        var process = new FakeServerProcess();
        var factory = new FakeServerProcessFactory();
        factory.Enqueue(process);
        var notifications = new System.Collections.Concurrent.ConcurrentQueue<Action>();
        var manager = new ServerRuntimeManager(factory, new FakeProcessTreeTerminator(),
            new RuntimeOperationCoordinator(), notifications.Enqueue);
        var runtime = manager.GetOrCreate(config);
        var statuses = new List<ServerStatus>();
        var crashes = 0;
        runtime.StatusChanged += statuses.Add;
        manager.ServerCrashed += (_, _) => Interlocked.Increment(ref crashes);
        await manager.StartAsync(config, directory.Root).WaitAsync(RuntimeTestDirectory.Timeout);
        process.EmitExit(7);
        await DrainOperationsAsync(manager);
        Assert.Equal(ServerStatus.Stopped, runtime.Status);
        await manager.StartAsync(config, directory.Root).WaitAsync(RuntimeTestDirectory.Timeout);
        Assert.Equal(ServerStatus.Starting, runtime.Status);
        Assert.Empty(statuses);

        while (notifications.TryDequeue(out var notify))
            notify();

        Assert.NotEmpty(statuses);
        Assert.All(statuses, status => Assert.Equal(ServerStatus.Starting, status));
        Assert.Equal(0, crashes);
        Assert.Equal(2, factory.CreateCalls);
    }

    [Fact]
    public async Task AmbiguousLaunchExceptionRetainsHandleAndPreventsSecondLaunch()
    {
        using var directory = new RuntimeTestDirectory();
        var config = directory.CreateConfig();
        var process = new FakeServerProcess
        {
            StartBehavior = () => throw new InvalidOperationException("Synthetic ambiguous launch failure")
        };
        var factory = new FakeServerProcessFactory();
        factory.Enqueue(process);
        var manager = CreateManager(factory);
        var runtime = manager.GetOrCreate(config);

        await Assert.ThrowsAsync<InvalidOperationException>(() => manager.StartAsync(config, directory.Root).WaitAsync(RuntimeTestDirectory.Timeout));

        Assert.Equal(ServerStatus.Unknown, runtime.Status);
        Assert.Equal(RuntimeLifetimeState.OwnedStopUncertain, runtime.Lifetime.State);
        Assert.Equal(0, process.DisposeCalls);
        Assert.False(manager.TryRelease(config.ServerId));
        await manager.StartAsync(config, directory.Root).WaitAsync(RuntimeTestDirectory.Timeout);
        Assert.Equal(1, factory.CreateCalls);
    }

    [Fact]
    public async Task ConfirmedStartFailureCanReleaseWithoutNativeProcessProbe()
    {
        using var directory = new RuntimeTestDirectory();
        var config = directory.CreateConfig();
        var process = new FakeServerProcess { StartBehavior = () => false };
        var factory = new FakeServerProcessFactory();
        factory.Enqueue(process);
        var manager = CreateManager(factory);
        var runtime = manager.GetOrCreate(config);

        await manager.StartAsync(config, directory.Root).WaitAsync(RuntimeTestDirectory.Timeout);

        Assert.Equal(ServerStatus.Stopped, runtime.Status);
        Assert.Equal(1, process.DisposeCalls);
        Assert.Null(runtime.Process);
        Assert.True(manager.TryRelease(config.ServerId));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task MutatingStoppedRuntimeIdentityCannotReleaseOrRegisterAnotherBinding(bool mutateServerId)
    {
        using var directory = new RuntimeTestDirectory();
        using var otherDirectory = new RuntimeTestDirectory();
        var config = directory.CreateConfig();
        var registeredId = config.ServerId;
        var factory = new FakeServerProcessFactory();
        var manager = CreateManager(factory);
        var runtime = manager.GetOrCreate(config);
        await manager.StartAsync(config, directory.Root).WaitAsync(RuntimeTestDirectory.Timeout);
        await manager.StopAsync(config).WaitAsync(RuntimeTestDirectory.Timeout);
        Assert.Equal(ServerStatus.Stopped, runtime.Status);
        if (mutateServerId)
            config.ServerId = "mutated-server-id";
        else
            config.DirectoryPath = otherDirectory.Root;

        Assert.False(manager.TryRelease(registeredId));
        Assert.Throws<InvalidOperationException>(() => manager.GetOrCreate(config));
        await Assert.ThrowsAsync<InvalidOperationException>(() => manager.StartAsync(config, config.DirectoryPath)
            .WaitAsync(RuntimeTestDirectory.Timeout));

        Assert.Equal(registeredId, runtime.RegisteredServerId);
        Assert.Equal(directory.Root, runtime.RegisteredDirectory);
        Assert.Equal(1, factory.CreateCalls);
        Assert.Equal(0, Assert.Single(factory.Created).DisposeCalls);
        config.ServerId = registeredId;
        config.DirectoryPath = directory.Root;
        Assert.True(manager.TryRelease(registeredId));
    }

    private static async Task DrainOperationsAsync(ServerRuntimeManager manager)
    {
        using var lease = await manager.Operations.EnterAsync().AsTask().WaitAsync(RuntimeTestDirectory.Timeout);
    }

    private static ServerRuntimeManager CreateManager(
        FakeServerProcessFactory factory,
        IProcessTreeTerminator? terminator = null,
        IRuntimeOperationCoordinator? operations = null,
        ManualRuntimeDelay? delay = null) =>
        new(factory, terminator ?? new FakeProcessTreeTerminator(),
            operations ?? new RuntimeOperationCoordinator(), notification => notification(), delay is null ? null : delay.DelayAsync);

    private static async Task WaitForStatusAsync(ServerRuntime runtime, ServerStatus expected, Action trigger)
    {
        var reached = FakeServerProcess.NewSignal();
        void Changed(ServerStatus status)
        {
            if (status == expected)
                reached.TrySetResult();
        }
        runtime.StatusChanged += Changed;
        try
        {
            trigger();
            if (runtime.Status == expected)
                reached.TrySetResult();
            await reached.Task.WaitAsync(RuntimeTestDirectory.Timeout);
        }
        finally
        {
            runtime.StatusChanged -= Changed;
        }
    }
}
