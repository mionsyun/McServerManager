using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Text;
using System.Threading;
using WpfApplication = System.Windows.Application;
using McServerManager.Models;
using McServerManager.Models.RuntimeLifecycle;
using McServerManager.Services.RuntimeLifecycle;

namespace McServerManager.Services;

public sealed class ServerRuntimeManager : IServerRuntimeManager
{
    private readonly Dictionary<string, ServerRuntime> _runtimes = new();
    private readonly object _runtimesLock = new();
    private readonly IServerProcessFactory _processFactory;
    private readonly IProcessTreeTerminator _terminator;
    private readonly Action<Action> _dispatchNotification;
    private readonly Func<TimeSpan, CancellationToken, Task> _delayAsync;

    public ServerRuntimeManager()
        : this(new SystemServerProcessFactory(), new ProcessTreeTerminator()) { }

    public ServerRuntimeManager(
        IServerProcessFactory processFactory,
        IProcessTreeTerminator terminator,
        IRuntimeOperationCoordinator? operations = null,
        Action<Action>? dispatchNotification = null,
        Func<TimeSpan, CancellationToken, Task>? delayAsync = null)
    {
        _processFactory = processFactory ?? throw new ArgumentNullException(nameof(processFactory));
        _terminator = terminator ?? throw new ArgumentNullException(nameof(terminator));
        Operations = operations ?? RuntimeOperationCoordinator.Shared;
        _dispatchNotification = dispatchNotification ?? ServerRuntime.DispatchOnUi;
        _delayAsync = delayAsync ?? ((delay, token) => Task.Delay(delay, token));
    }

    /// <summary>Exclusion only. Holding this lease never certifies process-tree or directory ownership.</summary>
    public IRuntimeOperationCoordinator Operations { get; }
    public event Action<ServerConfig, int?>? ServerCrashed;

    public ServerRuntime GetOrCreate(ServerConfig config)
    {
        ArgumentNullException.ThrowIfNull(config);
        lock (_runtimesLock)
        {
            if (_runtimes.Values.Any(existing => ReferenceEquals(existing.Config, config) &&
                !string.Equals(existing.RegisteredServerId, config.ServerId, StringComparison.Ordinal)))
                throw new InvalidOperationException("登録済み設定オブジェクトのサーバー ID は変更できません。");
            if (_runtimes.TryGetValue(config.ServerId, out var runtime))
            {
                runtime.UpdateConfig(config);
                return runtime;
            }

            runtime = new ServerRuntime(config, _dispatchNotification);
            _runtimes[config.ServerId] = runtime;
            return runtime;
        }
    }

    public bool TryRelease(string serverId)
    {
        if (string.IsNullOrWhiteSpace(serverId) || !Operations.TryEnter(out var lease))
            return false;

        using (lease)
        {
            ServerRuntime runtime;
            lock (_runtimesLock)
            {
                if (!_runtimes.TryGetValue(serverId, out runtime!) || !runtime.CanRelease)
                    return false;
                _runtimes.Remove(serverId);
            }
            runtime.CancelAutoRestart();
            runtime.DisposeExitedProcess();
            return true;
        }
    }

    public async Task StartAsync(ServerConfig config, string serverDirectory)
    {
        var runtime = GetOrCreate(config);
        runtime.CancelAutoRestart();
        using var lease = await Operations.EnterAsync().ConfigureAwait(false);
        runtime.CancelAutoRestart();
        await StartCoreAsync(runtime, config, serverDirectory).ConfigureAwait(false);
    }

    private Task StartCoreAsync(ServerRuntime runtime, ServerConfig config, string serverDirectory)
    {
        EnsureRegistered(runtime);
        runtime.ValidateDirectory(serverDirectory);
        if (runtime.Status != ServerStatus.Stopped)
            return Task.CompletedTask;

        lock (_runtimesLock)
        {
            if (_runtimes.Values.Any(other => !ReferenceEquals(other, runtime) &&
                string.Equals(other.RegisteredDirectory, runtime.RegisteredDirectory, StringComparison.OrdinalIgnoreCase) &&
                (other.Status != ServerStatus.Stopped || !other.CanRelease)))
                throw new InvalidOperationException("同じディレクトリを使用する別のランタイムが起動中、または停止未確認です。");
        }

        var (startInfo, strategyLabel) = ServerEditions.IsBedrock(config)
            ? BuildBedrockStartInfo(runtime.RegisteredDirectory)
            : BuildJavaStartInfo(config, runtime.RegisteredDirectory);

        runtime.DisposeExitedProcess();
        var process = _processFactory.Create(startInfo);
        var generation = runtime.AttachProcess(process,
            string.Equals(Path.GetFileName(startInfo.FileName), "cmd.exe", StringComparison.OrdinalIgnoreCase));
        process.OutputReceived += data => ObserveOutput(runtime, process, generation, data);
        process.Exited += () => ObserveExit(runtime, process, generation);
        runtime.SetStatus(ServerStatus.Starting);
        runtime.AddLog("起動中...");
        runtime.AddLog($"起動方式: {strategyLabel}");

        bool started;
        try
        {
            started = process.Start();
        }
        catch (Win32Exception ex)
        {
            // Native process creation failed: no launched process was transferred to this handle.
            runtime.DetachFailedLaunch();
            process.Dispose();
            runtime.SetStatus(ServerStatus.Stopped);
            runtime.AddLog($"起動に失敗しました: {ex.Message}");
            if (!ServerEditions.IsBedrock(config))
                throw new JavaNotFoundException(startInfo.FileName, ex);
            throw;
        }
        catch (Exception ex)
        {
            // An unexpected launcher failure may occur after process creation. Retain the handle.
            runtime.MarkStopUncertain();
            runtime.SetStatus(ServerStatus.Unknown);
            runtime.AddLog($"起動の完了を確認できませんでした: {ex.Message}");
            throw;
        }

        if (!started)
        {
            runtime.DetachFailedLaunch();
            process.Dispose();
            runtime.SetStatus(ServerStatus.Stopped);
            runtime.AddLog("起動に失敗しました。");
            return Task.CompletedTask;
        }

        runtime.MarkLaunchOwned();
        try
        {
            process.BeginOutputRead();
        }
        catch (Exception ex)
        {
            // The process was already launched: keep ownership and allow an explicit stop retry.
            runtime.MarkStopUncertain();
            runtime.SetStatus(ServerStatus.Unknown);
            runtime.AddLog($"起動プロセスは作成されましたが、出力の監視に失敗しました: {ex.Message}");
            throw;
        }
        return Task.CompletedTask;
    }

    private static (ProcessStartInfo StartInfo, string StrategyLabel) BuildJavaStartInfo(
        ServerConfig config,
        string serverDirectory)
    {
        var javaPath = string.IsNullOrWhiteSpace(config.JavaPath) ? "java" : config.JavaPath;
        var jarPath = Path.Combine(serverDirectory, "server.jar");

        // Forge 1.17+ は run.bat / win_args.txt で起動するため server.jar が不要な場合がある
        var isForge = string.Equals(config.Type, "Forge", StringComparison.OrdinalIgnoreCase);
        var hasForgeAltLaunch = isForge && (
            File.Exists(Path.Combine(serverDirectory, "run.bat")) ||
            !string.IsNullOrWhiteSpace(FindForgeWinArgsFile(serverDirectory)));

        if (!File.Exists(jarPath) && !hasForgeAltLaunch)
        {
            throw new FileNotFoundException("server.jar が見つかりません。", jarPath);
        }

        return BuildStartInfo(config, serverDirectory, javaPath, jarPath);
    }

    /// <summary>統合版は Java 不要。bedrock_server.exe を引数なしで起動する（設定は server.properties から読まれる）。</summary>
    private static (ProcessStartInfo StartInfo, string StrategyLabel) BuildBedrockStartInfo(string serverDirectory)
    {
        var exePath = Path.Combine(serverDirectory, ServerEditions.BedrockExecutableName);
        if (!File.Exists(exePath))
        {
            throw new FileNotFoundException(
                "bedrock_server.exe が見つかりません。「バージョン」画面から統合版サーバーを再インストールしてください。",
                exePath);
        }

        return (CreateBaseStartInfo(exePath, string.Empty, serverDirectory), "統合版 bedrock_server.exe");
    }

    private static (ProcessStartInfo StartInfo, string StrategyLabel) BuildStartInfo(
        ServerConfig config,
        string serverDirectory,
        string javaPath,
        string jarPath)
    {
        var extraJavaArgs = string.IsNullOrWhiteSpace(config.JavaExtraArguments)
            ? string.Empty
            : $" {config.JavaExtraArguments.Trim()}";

        if (string.Equals(config.LaunchModeOverride, "ForceForgeRunBat", StringComparison.OrdinalIgnoreCase))
        {
            var forcedRunBat = Path.Combine(serverDirectory, "run.bat");
            if (!File.Exists(forcedRunBat))
            {
                throw new FileNotFoundException("起動方式が run.bat 固定ですが run.bat が見つかりません。", forcedRunBat);
            }

            return (CreateBaseStartInfo(
                    "cmd.exe",
                    $"/c \"\"{forcedRunBat}\"\"",
                    serverDirectory),
                "手動固定: Forge run.bat");
        }

        if (string.Equals(config.LaunchModeOverride, "ForceForgeWinArgs", StringComparison.OrdinalIgnoreCase))
        {
            var forcedWinArgs = FindForgeWinArgsFile(serverDirectory);
            if (string.IsNullOrWhiteSpace(forcedWinArgs))
            {
                throw new FileNotFoundException("起動方式が win_args 固定ですが win_args.txt が見つかりません。", serverDirectory);
            }

            return (CreateBaseStartInfo(
                    javaPath,
                    $"-Xms{config.MemoryXmsMb}M -Xmx{config.MemoryXmxMb}M{extraJavaArgs} @\"{forcedWinArgs}\" nogui",
                    serverDirectory),
                "手動固定: Forge win_args.txt");
        }

        if (string.Equals(config.LaunchModeOverride, "ForceServerJar", StringComparison.OrdinalIgnoreCase))
        {
            return (CreateBaseStartInfo(
                    javaPath,
                    $"-Xms{config.MemoryXmsMb}M -Xmx{config.MemoryXmxMb}M{extraJavaArgs} -jar \"{jarPath}\" nogui",
                    serverDirectory),
                "手動固定: 標準 server.jar");
        }

        if (string.Equals(config.Type, "Forge", StringComparison.OrdinalIgnoreCase))
        {
            var runBat = Path.Combine(serverDirectory, "run.bat");
            if (File.Exists(runBat))
            {
                return (CreateBaseStartInfo(
                        "cmd.exe",
                        $"/c \"\"{runBat}\"\"",
                        serverDirectory),
                    "Forge run.bat");
            }

            var winArgsPath = FindForgeWinArgsFile(serverDirectory);
            if (!string.IsNullOrWhiteSpace(winArgsPath))
            {
                return (CreateBaseStartInfo(
                        javaPath,
                    $"-Xms{config.MemoryXmsMb}M -Xmx{config.MemoryXmxMb}M{extraJavaArgs} @\"{winArgsPath}\" nogui",
                        serverDirectory),
                    "Forge win_args.txt");
            }
        }

        return (CreateBaseStartInfo(
                javaPath,
                $"-Xms{config.MemoryXmsMb}M -Xmx{config.MemoryXmxMb}M{extraJavaArgs} -jar \"{jarPath}\" nogui",
                serverDirectory),
            "標準 server.jar");
    }

    private static ProcessStartInfo CreateBaseStartInfo(string fileName, string arguments, string workingDirectory)
    {
        return new ProcessStartInfo
        {
            FileName = fileName,
            Arguments = arguments,
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8
        };
    }

    private static string? FindForgeWinArgsFile(string serverDirectory)
    {
        try
        {
            return Directory.EnumerateFiles(serverDirectory, "win_args.txt", SearchOption.AllDirectories)
                .OrderByDescending(path => File.GetLastWriteTimeUtc(path))
                .FirstOrDefault();
        }
        catch
        {
            return null;
        }
    }

    public async Task StopAsync(ServerConfig config, int timeoutSeconds = 10)
    {
        var runtime = GetOrCreate(config);
        // Cancel before waiting for the gate as well as after acquiring it.
        runtime.CancelAutoRestart();
        using var lease = await Operations.EnterAsync().ConfigureAwait(false);
        runtime.CancelAutoRestart();
        await StopCoreAsync(runtime, timeoutSeconds).ConfigureAwait(false);
    }

    private async Task StopCoreAsync(ServerRuntime runtime, int timeoutSeconds)
    {
        EnsureRegistered(runtime);
        runtime.ExpectExit();
        var process = runtime.OwnedProcess;
        if (runtime.Status == ServerStatus.Stopped)
            return;
        if (process is null)
        {
            runtime.MarkStopUncertain();
            runtime.SetStatus(ServerStatus.Unknown);
            runtime.AddLog("所有する起動プロセスがないため、停止を確認できません。");
            return;
        }

        runtime.SetStopForced(false);
        runtime.SetStatus(ServerStatus.Stopping);
        runtime.AddLog("停止中...");
        ProcessStopResult result;
        try
        {
            result = await _terminator.StopAsync(process,
                TimeSpan.FromSeconds(Math.Clamp(timeoutSeconds, 0, 300))).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            result = new ProcessStopResult(ProcessStopOutcome.Uncertain, false, ex.Message);
        }

        runtime.SetStopForced(result.Forced);
        runtime.SetStopResult(result);
        if (result.Outcome == ProcessStopOutcome.RootExitedUnverified && !runtime.Lifetime.UsesShellWrapper)
        {
            runtime.MarkRootExited();
            runtime.SetStatus(ServerStatus.Stopped);
        }
        else
        {
            runtime.MarkStopUncertain();
            runtime.SetStatus(ServerStatus.Unknown);
            runtime.AddLog(result.Error ?? "起動用シェルの終了だけでは子サーバーの停止を確認できません。");
        }
    }

    public async Task RestartAsync(ServerConfig config, string serverDirectory)
    {
        var runtime = GetOrCreate(config);
        runtime.CancelAutoRestart();
        using var lease = await Operations.EnterAsync().ConfigureAwait(false);
        runtime.CancelAutoRestart();
        runtime.ValidateDirectory(serverDirectory);
        await StopCoreAsync(runtime, 10).ConfigureAwait(false);
        if (runtime.Status != ServerStatus.Stopped)
            throw new InvalidOperationException("停止を確認できないため、再起動を中止しました。");
        await StartCoreAsync(runtime, config, serverDirectory).ConfigureAwait(false);
    }

    public void SendCommand(ServerConfig config, string command)
    {
        var runtime = GetOrCreate(config);
        _ = SendCommandCoreAsync(runtime, command);
    }

    private async Task SendCommandCoreAsync(ServerRuntime runtime, string command)
    {
        try
        {
            using var lease = await Operations.EnterAsync().ConfigureAwait(false);
            EnsureRegistered(runtime);
            if (runtime.Status is ServerStatus.Starting or ServerStatus.Running)
                await runtime.SendCommandAsync(command).WaitAsync(TimeSpan.FromSeconds(10)).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            runtime.AddLog($"コマンドの送信に失敗しました: {ex.Message}");
        }
    }

    private void ObserveOutput(ServerRuntime runtime, IServerProcess process, long generation, string? data)
    {
        if (!string.IsNullOrWhiteSpace(data) && runtime.TryQueueOutput(process, generation, data, IsStartupCompleteLine(runtime.Config, data)))
            _ = HandleOutputAsync(runtime, process, generation);
    }

    private async Task HandleOutputAsync(ServerRuntime runtime, IServerProcess process, long generation)
    {
        try
        {
            using var lease = await Operations.EnterAsync().ConfigureAwait(false);
            if (!runtime.IsCurrent(process, generation))
                return;
            var lines = runtime.DrainOutput(process, generation, out var startupComplete);
            if (runtime.Status == ServerStatus.Starting && startupComplete)
                runtime.SetStatus(ServerStatus.Running);
            foreach (var data in lines)
                runtime.AddLog(data);
        }
        catch (Exception ex)
        {
            runtime.AddLog($"出力の処理に失敗しました: {ex.Message}");
        }
    }

    private static bool IsStartupCompleteLine(ServerConfig config, string line) =>
        ServerEditions.IsBedrock(config)
            ? line.Contains("Server started.", StringComparison.OrdinalIgnoreCase)
            : line.Contains("Done (", StringComparison.OrdinalIgnoreCase);

    private void ObserveExit(ServerRuntime runtime, IServerProcess process, long generation) =>
        _ = HandleProcessExitedAsync(runtime, process, generation);

    private async Task HandleProcessExitedAsync(ServerRuntime runtime, IServerProcess process, long generation)
    {
        try
        {
            using var lease = await Operations.EnterAsync().ConfigureAwait(false);
            if (!runtime.IsCurrent(process, generation) || !runtime.TryHandleExit())
                return;

            // Stop intent persists after Unknown, so a late event cannot become a crash/restart.
            if (runtime.ExitExpected)
                return;
            if (runtime.Lifetime.UsesShellWrapper || runtime.Lifetime.State == RuntimeLifetimeState.OwnedStopUncertain)
            {
                runtime.MarkStopUncertain();
                runtime.SetStatus(ServerStatus.Unknown);
                runtime.AddLog("起動プロセスが終了しましたが、サーバー全体の停止状態は不明です。");
                return;
            }

            var exitCode = process.ExitCode;
            runtime.MarkRootExited();
            runtime.SetStatus(ServerStatus.Stopped);
            runtime.AddLog($"サーバーが予期せず終了しました (ExitCode={exitCode})。");
            runtime.PostNotification(() =>
            {
                if (!runtime.IsCurrent(process, generation))
                    return;
                foreach (Action<ServerConfig, int?> subscriber in ServerCrashed?.GetInvocationList() ?? Array.Empty<Delegate>())
                {
                    try { subscriber(runtime.Config, exitCode); }
                    catch (Exception ex) { Trace.TraceError($"Runtime crash subscriber failed: {ex.Message}"); }
                }
            });
            if (runtime.Config.AutoRestartOnCrash)
                ScheduleAutoRestart(runtime, generation);
        }
        catch (Exception ex)
        {
            // Never let an obsolete callback poison a replacement's status.
            using var lease = await Operations.EnterAsync().ConfigureAwait(false);
            if (runtime.IsCurrent(process, generation))
            {
                runtime.MarkStopUncertain();
                runtime.SetStatus(ServerStatus.Unknown);
                runtime.AddLog($"終了状態の確認に失敗しました: {ex.Message}");
            }
        }
    }

    private void ScheduleAutoRestart(ServerRuntime runtime, long generation)
    {
        var delaySeconds = Math.Clamp(runtime.Config.AutoRestartDelaySeconds, 1, 300);
        var ticket = runtime.CreateAutoRestartTicket();
        runtime.AddLog($"自動再起動を{delaySeconds}秒後に実行します。");
        _ = RunAutoRestartAsync(runtime, generation, ticket, delaySeconds);
    }

    private async Task RunAutoRestartAsync(
        ServerRuntime runtime, long generation, CancellationTokenSource ticket, int delaySeconds)
    {
        try
        {
            await _delayAsync(TimeSpan.FromSeconds(delaySeconds), ticket.Token).ConfigureAwait(false);
            using var lease = await Operations.EnterAsync(ticket.Token).ConfigureAwait(false);
            if (!runtime.IsAutoRestartCurrent(ticket, generation) || !IsRegistered(runtime) ||
                runtime.Status != ServerStatus.Stopped || !runtime.Config.AutoRestartOnCrash)
                return;
            runtime.CompleteAutoRestart(ticket);
            await StartCoreAsync(runtime, runtime.Config, runtime.RegisteredDirectory).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (ticket.IsCancellationRequested) { }
        catch (Exception ex)
        {
            runtime.AddLog($"自動再起動に失敗しました: {ex.Message}");
        }
        finally
        {
            runtime.CompleteAutoRestart(ticket);
            ticket.Dispose();
        }
    }

    private void EnsureRegistered(ServerRuntime runtime)
    {
        if (!IsRegistered(runtime))
            throw new InvalidOperationException("このランタイムは既に解放されています。");
        runtime.UpdateConfig(runtime.Config);
    }

    private bool IsRegistered(ServerRuntime runtime)
    {
        lock (_runtimesLock)
            return _runtimes.TryGetValue(runtime.RegisteredServerId, out var registered) && ReferenceEquals(runtime, registered);
    }
}

public sealed class ServerRuntime
{
    private readonly object _lock = new();
    // CollectionChanged callbacks must never run while the runtime state lock is held.
    private readonly object _logsLock = new();
    private readonly Action<Action> _dispatchNotification;
    private IServerProcess? _process;
    private CancellationTokenSource? _autoRestart;
    private long _generation;
    private volatile ServerStatus _status = ServerStatus.Stopped;
    private volatile RuntimeLifetimeSnapshot _lifetime;
    private bool _exitHandled;
    private readonly Queue<string> _pendingOutput = new();
    private bool _outputWorkerScheduled;
    private bool _startupSignalQueued;
    private int _droppedOutput;
    private readonly Queue<(string Line, string Message, int Limit)> _pendingLogs = new();
    private bool _logDrainScheduled;
    private int _droppedLogs;

    public ServerRuntime(ServerConfig config, Action<Action>? dispatchNotification = null)
    {
        Config = config;
        RegisteredServerId = config.ServerId;
        RegisteredDirectory = NormalizeDirectory(config.DirectoryPath);
        _lifetime = new RuntimeLifetimeSnapshot(0, RuntimeLifetimeState.Unknown, RegisteredDirectory, false);
        _dispatchNotification = dispatchNotification ?? DispatchOnUi;
        Logs = new ObservableCollection<string>();
    }

    public ServerConfig Config { get; private set; }
    public string RegisteredServerId { get; }
    public string RegisteredDirectory { get; private set; }
    public ServerStatus Status => _status;
    public RuntimeLifetimeSnapshot Lifetime => _lifetime;
    public ProcessStopResult? LastStopResult { get; private set; }
    public ObservableCollection<string> Logs { get; }
    public Process? Process { get { lock (_lock) return _process?.NativeProcess; } }
    internal IServerProcess? OwnedProcess { get { lock (_lock) return _process; } }
    internal bool ExitExpected { get; private set; }
    internal bool CanRelease => HasMatchingConfigIdentity && Status == ServerStatus.Stopped &&
        (OwnedProcess is null || Lifetime.State == RuntimeLifetimeState.OwnedRootExitedUnverified);
    private bool HasMatchingConfigIdentity
    {
        get
        {
            try
            {
                return string.Equals(Config.ServerId, RegisteredServerId, StringComparison.Ordinal) &&
                    string.Equals(NormalizeDirectory(Config.DirectoryPath), RegisteredDirectory, StringComparison.OrdinalIgnoreCase);
            }
            catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
            {
                return false;
            }
        }
    }
    public event Action<ServerStatus>? StatusChanged;
    public event Action<string>? LogReceived;
    public bool LastStopWasForced { get; private set; }

    public void UpdateConfig(ServerConfig config)
    {
        lock (_lock)
        {
            if (!string.Equals(config.ServerId, RegisteredServerId, StringComparison.Ordinal) ||
                !string.Equals(NormalizeDirectory(config.DirectoryPath), RegisteredDirectory, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("登録済みランタイムのサーバー ID またはディレクトリは変更できません。");
            Config = config;
        }
    }

    internal void ValidateDirectory(string directory)
    {
        var normalized = NormalizeDirectory(directory);
        if (string.IsNullOrEmpty(normalized) ||
            !string.Equals(normalized, RegisteredDirectory, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("起動先が登録済みサーバーディレクトリと一致しません。");
        // Config is mutable: also reject mutations made to the originally registered object.
        UpdateConfig(Config);
    }

    private static string NormalizeDirectory(string directory) =>
        string.IsNullOrWhiteSpace(directory) ? string.Empty : Path.TrimEndingDirectorySeparator(Path.GetFullPath(directory));

    internal long AttachProcess(IServerProcess process, bool usesShellWrapper)
    {
        lock (_lock)
        {
            _process = process;
            _generation++;
            ExitExpected = false;
            _exitHandled = false;
            _pendingOutput.Clear();
            _outputWorkerScheduled = false;
            _startupSignalQueued = false;
            _droppedOutput = 0;
            LastStopResult = null;
            LastStopWasForced = false;
            _lifetime = new RuntimeLifetimeSnapshot(_generation, RuntimeLifetimeState.Unknown, RegisteredDirectory, usesShellWrapper);
            return _generation;
        }
    }

    internal bool IsCurrent(IServerProcess process, long generation)
    {
        lock (_lock)
            return ReferenceEquals(_process, process) && _generation == generation;
    }

    internal void DetachFailedLaunch()
    {
        lock (_lock)
        {
            _process = null;
            _lifetime = _lifetime with { State = RuntimeLifetimeState.Unknown };
        }
    }

    internal void DisposeExitedProcess()
    {
        IServerProcess? process;
        lock (_lock)
        {
            process = _process;
            if (process is not null && _lifetime.State != RuntimeLifetimeState.OwnedRootExitedUnverified)
                throw new InvalidOperationException("終了未確認のプロセス所有権は解放できません。");
            _process = null;
        }
        process?.Dispose();
    }

    internal void MarkLaunchOwned() => _lifetime = _lifetime with { State = RuntimeLifetimeState.OwnedRunning };
    internal void MarkRootExited() => _lifetime = _lifetime with { State = RuntimeLifetimeState.OwnedRootExitedUnverified };
    internal void MarkStopUncertain() => _lifetime = _lifetime with { State = RuntimeLifetimeState.OwnedStopUncertain };
    internal void ExpectExit() => ExitExpected = true;
    internal bool TryHandleExit()
    {
        if (_exitHandled) return false;
        _exitHandled = true;
        return true;
    }

    internal void SetStatus(ServerStatus status)
    {
        _status = status;
        // Read authoritative status when the queued UI notification runs, not a stale transition.
        PostNotification(() =>
        {
            foreach (Action<ServerStatus> subscriber in StatusChanged?.GetInvocationList() ?? Array.Empty<Delegate>())
            {
                try { subscriber(Status); }
                catch (Exception ex) { Trace.TraceError($"Runtime status subscriber failed: {ex.Message}"); }
            }
        });
    }

    internal void SetStopForced(bool forced) => LastStopWasForced = forced;
    internal void SetStopResult(ProcessStopResult result) => LastStopResult = result;

    internal void CancelAutoRestart()
    {
        lock (_lock)
        {
            _autoRestart?.Cancel();
            _autoRestart = null;
        }
    }

    internal CancellationTokenSource CreateAutoRestartTicket()
    {
        lock (_lock)
        {
            _autoRestart?.Cancel();
            return _autoRestart = new CancellationTokenSource();
        }
    }

    internal bool IsAutoRestartCurrent(CancellationTokenSource ticket, long generation)
    {
        lock (_lock)
            return ReferenceEquals(_autoRestart, ticket) && !ticket.IsCancellationRequested && _generation == generation;
    }

    internal void CompleteAutoRestart(CancellationTokenSource ticket)
    {
        lock (_lock)
        {
            if (ReferenceEquals(_autoRestart, ticket))
                _autoRestart = null;
        }
    }

    internal bool TryQueueOutput(IServerProcess process, long generation, string data, bool startupComplete)
    {
        lock (_lock)
        {
            if (!ReferenceEquals(_process, process) || _generation != generation)
                return false;
            // Lifecycle metadata survives dropping display-only output during a flood.
            _startupSignalQueued |= startupComplete;
            if (_pendingOutput.Count >= 256)
                _droppedOutput++;
            else
                _pendingOutput.Enqueue(data.Length <= 16384 ? data : data[..16384]);
            if (_outputWorkerScheduled)
                return false;
            _outputWorkerScheduled = true;
            return true;
        }
    }

    internal string[] DrainOutput(IServerProcess process, long generation, out bool startupComplete)
    {
        lock (_lock)
        {
            startupComplete = false;
            if (!ReferenceEquals(_process, process) || _generation != generation)
                return [];
            startupComplete = _startupSignalQueued;
            _startupSignalQueued = false;
            var lines = _pendingOutput.ToList();
            _pendingOutput.Clear();
            if (_droppedOutput > 0)
                lines.Add($"ログが集中したため {_droppedOutput} 行の表示を省略しました。");
            _droppedOutput = 0;
            _outputWorkerScheduled = false;
            return lines.ToArray();
        }
    }

    public void AddLog(string message, int maxLines = 5000)
    {
        if (message.Length > 16384)
            message = message[..16384];
        var line = $"[{DateTime.Now:HH:mm:ss}] {message}";
        lock (_lock)
        {
            if (_pendingLogs.Count >= 512)
                _droppedLogs++;
            else
                _pendingLogs.Enqueue((line, message, Math.Clamp(maxLines, 1, 5000)));
            if (_logDrainScheduled)
                return;
            _logDrainScheduled = true;
        }
        PostNotification(DrainLogs);
    }

    private void DrainLogs()
    {
        // At most one queued dispatcher callback per batch; the queue and each line are bounded.
        (string Line, string Message, int Limit)[] batch;
        int dropped;
        lock (_lock)
        {
            batch = _pendingLogs.ToArray();
            _pendingLogs.Clear();
            dropped = _droppedLogs;
            _droppedLogs = 0;
            _logDrainScheduled = false;
        }
        foreach (var entry in batch)
        {
            lock (_logsLock)
            {
                Logs.Add(entry.Line);
                while (Logs.Count > entry.Limit)
                    Logs.RemoveAt(0);
            }
            foreach (Action<string> subscriber in LogReceived?.GetInvocationList() ?? Array.Empty<Delegate>())
            {
                try { subscriber(entry.Message); }
                catch (Exception ex) { Trace.TraceError($"Runtime log subscriber failed: {ex.Message}"); }
            }
        }
        if (dropped > 0)
        {
            lock (_logsLock)
            {
                Logs.Add($"[{DateTime.Now:HH:mm:ss}] 表示待ちのログ {dropped} 行を省略しました。");
                while (Logs.Count > 5000)
                    Logs.RemoveAt(0);
            }
        }
    }

    internal void PostNotification(Action action)
    {
        try
        {
            _dispatchNotification(() =>
            {
                try { action(); }
                catch (Exception ex) { Trace.TraceError($"Runtime notification failed: {ex.Message}"); }
            });
        }
        catch (Exception ex) { Trace.TraceError($"Runtime notification dispatch failed: {ex.Message}"); }
    }

    internal static void DispatchOnUi(Action action)
    {
        var dispatcher = WpfApplication.Current?.Dispatcher;
        if (dispatcher is null)
            action();
        else if (!dispatcher.HasShutdownStarted && dispatcher.Thread.IsAlive)
            _ = dispatcher.BeginInvoke(action);
    }

    internal Task SendCommandAsync(string command) => OwnedProcess?.SendCommandAsync(command) ?? Task.CompletedTask;
}
