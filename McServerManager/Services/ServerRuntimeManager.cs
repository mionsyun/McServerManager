using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Text;
using WpfApplication = System.Windows.Application;
using McServerManager.Models;

namespace McServerManager.Services;

public sealed class ServerRuntimeManager
{
    private readonly Dictionary<string, ServerRuntime> _runtimes = new();
    private readonly object _runtimesLock = new();

    public event Action<ServerConfig, int?>? ServerCrashed;

    public ServerRuntime GetOrCreate(ServerConfig config)
    {
        lock (_runtimesLock)
        {
            if (_runtimes.TryGetValue(config.ServerId, out var runtime))
            {
                runtime.UpdateConfig(config);
                return runtime;
            }

            runtime = new ServerRuntime(config);
            _runtimes[config.ServerId] = runtime;
            return runtime;
        }
    }

    public bool TryRelease(string serverId)
    {
        if (string.IsNullOrWhiteSpace(serverId))
        {
            return false;
        }

        lock (_runtimesLock)
        {
            if (!_runtimes.TryGetValue(serverId, out var runtime))
            {
                return false;
            }

            var process = runtime.Process;
            if (runtime.Status != ServerStatus.Stopped || (process is not null && !process.HasExited))
            {
                return false;
            }

            _runtimes.Remove(serverId);
            return true;
        }
    }

    public async Task StartAsync(ServerConfig config, string serverDirectory)
    {
        var runtime = GetOrCreate(config);
        if (runtime.Status != ServerStatus.Stopped)
        {
            return;
        }

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

        runtime.SetStatus(ServerStatus.Starting);
        runtime.AddLog("起動中...");
        var (startInfo, strategyLabel) = BuildStartInfo(config, serverDirectory, javaPath, jarPath);
        runtime.AddLog($"起動方式: {strategyLabel}");

        var process = new Process
        {
            StartInfo = startInfo,
            EnableRaisingEvents = true
        };

        process.OutputDataReceived += (_, args) => HandleOutput(runtime, args.Data);
        process.ErrorDataReceived += (_, args) => HandleOutput(runtime, args.Data);
        process.Exited += (_, _) => HandleProcessExited(runtime, process.ExitCode);

        runtime.AttachProcess(process);

        if (!process.Start())
        {
            runtime.SetStatus(ServerStatus.Stopped);
            runtime.AddLog("起動に失敗しました。");
            return;
        }

        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        await Task.Delay(300).ConfigureAwait(false);
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
        if (runtime.Status == ServerStatus.Stopped || runtime.Process is null)
        {
            return;
        }

        runtime.SetStopForced(false);
        runtime.SetStatus(ServerStatus.Stopping);
        runtime.AddLog("停止中...");

        try
        {
            await runtime.SendCommandAsync("stop").ConfigureAwait(false);
        }
        catch
        {
            // ignore
        }

        var process = runtime.Process;
        var exited = await Task.Run(() => process.WaitForExit(timeoutSeconds * 1000)).ConfigureAwait(false);
        if (!exited)
        {
            runtime.AddLog("停止タイムアウト。強制終了します。");
            try
            {
                process.Kill(true);
                process.WaitForExit(Math.Max(1000, timeoutSeconds * 1000));
                runtime.SetStopForced(true);
            }
            catch (Exception ex)
            {
                runtime.AddLog($"強制終了に失敗しました: {ex.Message}");
            }
        }

        runtime.SetStatus(ServerStatus.Stopped);
    }

    public async Task RestartAsync(ServerConfig config, string serverDirectory)
    {
        await StopAsync(config).ConfigureAwait(false);
        await StartAsync(config, serverDirectory).ConfigureAwait(false);
    }

    public void SendCommand(ServerConfig config, string command)
    {
        var runtime = GetOrCreate(config);
        _ = runtime.SendCommandAsync(command);
    }

    private void HandleOutput(ServerRuntime runtime, string? data)
    {
        if (string.IsNullOrWhiteSpace(data))
        {
            return;
        }

        if (runtime.Status == ServerStatus.Starting && data.Contains("Done (", StringComparison.OrdinalIgnoreCase))
        {
            runtime.SetStatus(ServerStatus.Running);
        }

        runtime.AddLog(data);
    }

    private void HandleProcessExited(ServerRuntime runtime, int exitCode)
    {
        var wasStopping = runtime.Status == ServerStatus.Stopping;
        runtime.SetStatus(ServerStatus.Stopped);

        if (!wasStopping)
        {
            runtime.AddLog($"サーバーが予期せず終了しました (ExitCode={exitCode})。");
            ServerCrashed?.Invoke(runtime.Config, exitCode);
            if (runtime.Config.AutoRestartOnCrash)
            {
                ScheduleAutoRestart(runtime);
            }
        }
    }

    private void ScheduleAutoRestart(ServerRuntime runtime)
    {
        var delaySeconds = Math.Clamp(runtime.Config.AutoRestartDelaySeconds, 1, 300);
        runtime.AddLog($"自動再起動を{delaySeconds}秒後に実行します。");
        _ = Task.Run(async () =>
        {
            await Task.Delay(TimeSpan.FromSeconds(delaySeconds)).ConfigureAwait(false);
            try
            {
                await StartAsync(runtime.Config, runtime.Config.DirectoryPath).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                runtime.AddLog($"自動再起動に失敗しました: {ex.Message}");
            }
        });
    }
}

public sealed class ServerRuntime
{
    private readonly object _lock = new();
    private Process? _process;

    public ServerRuntime(ServerConfig config)
    {
        Config = config;
        Logs = new ObservableCollection<string>();
    }

    public ServerConfig Config { get; private set; }
    public ServerStatus Status { get; private set; } = ServerStatus.Stopped;
    public ObservableCollection<string> Logs { get; }
    public Process? Process => _process;
    public event Action<ServerStatus>? StatusChanged;
    public event Action<string>? LogReceived;
    public bool LastStopWasForced { get; private set; }

    public void UpdateConfig(ServerConfig config)
    {
        Config = config;
    }

    public void AttachProcess(Process process)
    {
        lock (_lock)
        {
            _process = process;
        }
    }

    public void SetStatus(ServerStatus status)
    {
        Status = status;
        StatusChanged?.Invoke(status);
    }

    public void SetStopForced(bool forced)
    {
        LastStopWasForced = forced;
    }

    public void AddLog(string message, int maxLines = 5000)
    {
        var timestamp = DateTime.Now.ToString("HH:mm:ss");
        var line = $"[{timestamp}] {message}";
        LogReceived?.Invoke(message);
        WpfApplication.Current.Dispatcher.Invoke(() =>
        {
            Logs.Add(line);
            while (Logs.Count > maxLines)
            {
                Logs.RemoveAt(0);
            }
        });
    }

    public Task SendCommandAsync(string command)
    {
        Process? process;
        lock (_lock)
        {
            process = _process;
        }

        if (process is null || process.HasExited)
        {
            return Task.CompletedTask;
        }

        return WriteCommandAsync(process, command);
    }

    private static async Task WriteCommandAsync(Process process, string command)
    {
        try
        {
            await process.StandardInput.WriteLineAsync(command).ConfigureAwait(false);
            await process.StandardInput.FlushAsync().ConfigureAwait(false);
        }
        catch (ObjectDisposedException)
        {
            // Process stream was already disposed.
        }
        catch (InvalidOperationException)
        {
            // Process ended before writing.
        }
        catch (IOException)
        {
            // Stream became unavailable during shutdown.
        }
    }
}
