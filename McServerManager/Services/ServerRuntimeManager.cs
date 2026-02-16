using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Text;
using WpfApplication = System.Windows.Application;
using McServerManager.Models;

namespace McServerManager.Services;

public sealed class ServerRuntimeManager
{
    private readonly Dictionary<string, ServerRuntime> _runtimes = new();

    public event Action<ServerConfig, int?>? ServerCrashed;

    public ServerRuntime GetOrCreate(ServerConfig config)
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

    public async Task StartAsync(ServerConfig config, string serverDirectory)
    {
        var runtime = GetOrCreate(config);
        if (runtime.Status != ServerStatus.Stopped)
        {
            return;
        }

        var javaPath = string.IsNullOrWhiteSpace(config.JavaPath) ? "java" : config.JavaPath;
        var jarPath = Path.Combine(serverDirectory, "server.jar");
        if (!File.Exists(jarPath))
        {
            throw new FileNotFoundException("server.jar が見つかりません。", jarPath);
        }

        runtime.SetStatus(ServerStatus.Starting);
        runtime.AddLog("起動中...");

        var startInfo = new ProcessStartInfo
        {
            FileName = javaPath,
            Arguments = $"-Xms{config.MemoryXmsMb}M -Xmx{config.MemoryXmxMb}M -jar \"{jarPath}\" nogui",
            WorkingDirectory = serverDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8
        };

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
            process.Kill(true);
            runtime.SetStopForced(true);
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
            await StartAsync(runtime.Config, runtime.Config.DirectoryPath).ConfigureAwait(false);
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
        if (_process is null || _process.HasExited)
        {
            return Task.CompletedTask;
        }

        return _process.StandardInput.WriteLineAsync(command);
    }
}
