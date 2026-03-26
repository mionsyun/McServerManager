using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Management;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Threading;
using McServerManager.Models;
using McServerManager.Services;
using McServerManager.Utilities;
using OpenFileDialog = Microsoft.Win32.OpenFileDialog;
using WpfApplication = System.Windows.Application;

namespace McServerManager.ViewModels;

public sealed class ServerViewModel : ObservableObject, IDisposable
{
    private static readonly Regex PlayerCountRegex = new(
        @"There are (\d+) of a max of (\d+) players online",
        RegexOptions.Compiled
    );
    private static readonly TimeSpan GpuSampleInterval = TimeSpan.FromSeconds(2);

    private readonly AppServices _services;
    private readonly ServerRuntime _runtime;
    private readonly AppSettings _appSettings;
    private readonly DispatcherTimer _statsTimer;
    private readonly ServerConfig _config;

    private ServerSettingsViewModel _settings;
    private string _commandText = string.Empty;
    private bool _restartRequired;
    private string _javaPath = string.Empty;
    private MinecraftVersionInfo? _selectedVersion;
    private VersionFilterOption? _selectedVersionFilter;
    private string _versionFilterSummary = string.Empty;
    private string _newOpName = string.Empty;
    private string _newWhitelistName = string.Empty;
    private OpEntry? _selectedOp;
    private WhitelistEntry? _selectedWhitelist;
    private bool _autoRestartOnCrash;
    private int _autoRestartDelaySeconds;
    private double _cpuUsagePercent;
    private double _memoryUsageMb;
    private double _gpuUsagePercent;
    private bool _gpuMonitoringAvailable = true;
    private int _onlinePlayers;
    private TimeSpan _lastCpuTime;
    private DateTime _lastCpuCheck;
    private DateTime _lastGpuCheck;
    private LaunchModeOption? _selectedLaunchMode;
    private StartupPresetOption? _selectedStartupPreset;
    private string _javaExtraArguments = string.Empty;
    private int _memoryXmsMb;
    private int _memoryXmxMb;
    private bool _isDisposed;
    private bool _isInitializing;
    private string _settingsSavedMessage = string.Empty;
    private bool _isVersionsLoading;
    private string _permissionsStatusMessage = string.Empty;

    public ServerViewModel(AppServices services, ServerConfig config)
    {
        _isInitializing = true;
        _services = services;
        _config = config;
        _runtime = _services.Runtime.GetOrCreate(config);
        _runtime.StatusChanged += OnStatusChanged;
        _runtime.LogReceived += OnLogReceived;
        _appSettings = _services.Settings.Load();

        _settings = new ServerSettingsViewModel();
        _settings.PropertyChanged += (_, _) => UpdateRestartRequired();

        Logs = _runtime.Logs;
        AvailableVersions = [];
        FilteredAvailableVersions = [];
        VersionFilters = [];
        Ops = [];
        Whitelist = [];
        LaunchModes = [];
        StartupPresets = [];

        _autoRestartOnCrash = _config.AutoRestartOnCrash;
        _autoRestartDelaySeconds = _config.AutoRestartDelaySeconds;
        _javaPath = _config.JavaPath;
        _memoryXmsMb = _config.MemoryXmsMb;
        _memoryXmxMb = _config.MemoryXmxMb;
        _javaExtraArguments = _config.JavaExtraArguments ?? string.Empty;
        InitializeLaunchOptions();
        InitializeVersionFilters();

        // サブViewModel の初期化
        World = new ServerWorldViewModel(_services, _config, _settings, () => Status);
        Addon = new ServerAddonViewModel(_services, _config);
        Network = new ServerNetworkViewModel(_services, _config, _appSettings);

        StartCommand = new AsyncRelayCommand(StartAsync, () => Status == ServerStatus.Stopped);
        StopCommand = new AsyncRelayCommand(StopAsync, () => Status != ServerStatus.Stopped);
        RestartCommand = new AsyncRelayCommand(RestartAsync, () => Status == ServerStatus.Running);
        SendCommandCommand = new RelayCommand(
            _ => SendCommand(),
            _ => Status == ServerStatus.Running
        );
        ExportLogsCommand = new RelayCommand(_ => ExportLogs());
        SaveSettingsCommand = new RelayCommand(_ => SaveSettings());
        BrowseJavaCommand = new RelayCommand(_ => BrowseJava());
        DetectJavaCommand = new RelayCommand(_ => DetectJava());
        OpenServerDirectoryCommand = new RelayCommand(_ => OpenServerDirectory());
        OpenLogsDirectoryCommand = new RelayCommand(_ => OpenLogsDirectory());
        OpenCrashReportsCommand = new RelayCommand(_ => OpenCrashReports());
        LoadVersionsCommand = new AsyncRelayCommand(() => LoadVersionsAsync(forceRefresh: true));
        ChangeVersionCommand = new AsyncRelayCommand(
            ChangeVersionAsync,
            () => SelectedVersion is not null
        );
        RedownloadJarCommand = new AsyncRelayCommand(RedownloadJarAsync);
        AddOpCommand = new RelayCommand(_ => AddOp(), _ => !string.IsNullOrWhiteSpace(NewOpName));
        RemoveOpCommand = new RelayCommand(_ => RemoveOp(), _ => SelectedOp is not null);
        AddWhitelistCommand = new RelayCommand(
            _ => AddWhitelist(),
            _ => !string.IsNullOrWhiteSpace(NewWhitelistName)
        );
        RemoveWhitelistCommand = new RelayCommand(
            _ => RemoveWhitelist(),
            _ => SelectedWhitelist is not null
        );
        ReloadPermissionsCommand = new RelayCommand(_ => LoadPermissions());
        ApplyStartupPresetCommand = new RelayCommand(
            _ => ApplyStartupPreset(),
            _ => SelectedStartupPreset is not null
        );
        RunHealthCheckCommand = new RelayCommand(_ => RunHealthCheck(showEvenIfCompleted: true));

        LoadSettings();
        LoadPermissions();
        _ = LoadVersionsAsync();

        _statsTimer = new DispatcherTimer(
            TimeSpan.FromSeconds(1),
            DispatcherPriority.Background,
            (_, _) => UpdateStats(),
            WpfApplication.Current.Dispatcher
        );
        _statsTimer.Start();

        Status = _runtime.Status;
        _isInitializing = false;
    }

    // ─── サブViewModel ─────────────────────────────────────────────
    public ServerWorldViewModel World { get; }
    public ServerAddonViewModel Addon { get; }
    public ServerNetworkViewModel Network { get; }

    // ─── 識別情報 ────────────────────────────────────────────────
    public string Name => _config.Name;
    public string ServerId => _config.ServerId;
    public string ServerType => _config.Type;
    public string Version => _config.Version;
    public int Port => _config.Port;
    public int MaxPlayers => _config.MaxPlayers;
    public string ServerDirectory => _config.DirectoryPath;

    // ─── コレクション ─────────────────────────────────────────────
    public ObservableCollection<string> Logs { get; }
    public ObservableCollection<MinecraftVersionInfo> AvailableVersions { get; }
    public ObservableCollection<MinecraftVersionInfo> FilteredAvailableVersions { get; }
    public ObservableCollection<VersionFilterOption> VersionFilters { get; }
    public ObservableCollection<OpEntry> Ops { get; }
    public ObservableCollection<WhitelistEntry> Whitelist { get; }
    public ObservableCollection<LaunchModeOption> LaunchModes { get; }
    public ObservableCollection<StartupPresetOption> StartupPresets { get; }

    public ServerSettingsViewModel Settings
    {
        get => _settings;
        private set => SetProperty(ref _settings, value);
    }

    // ─── Status ──────────────────────────────────────────────────
    private ServerStatus _status = ServerStatus.Stopped;
    public ServerStatus Status
    {
        get => _status;
        private set
        {
            if (SetProperty(ref _status, value))
            {
                OnPropertyChanged(nameof(StatusText));
                OnPropertyChanged(nameof(StartButtonLabel));
                OnPropertyChanged(nameof(StopButtonLabel));
                OnPropertyChanged(nameof(RestartButtonLabel));
                StartCommand.RaiseCanExecuteChanged();
                StopCommand.RaiseCanExecuteChanged();
                RestartCommand.RaiseCanExecuteChanged();
                SendCommandCommand.RaiseCanExecuteChanged();
                // サブViewModelに Status 変化を伝播
                World.OnServerStatusChanged();
            }
        }
    }

    public string StatusText =>
        Status switch
        {
            ServerStatus.Starting => "起動中",
            ServerStatus.Running => "稼働中",
            ServerStatus.Stopping => "停止中",
            _ => "停止",
        };

    public string StartButtonLabel => Status == ServerStatus.Starting ? "起動中..." : "▶ 起動";
    public string StopButtonLabel => Status == ServerStatus.Stopping ? "停止中..." : "■ 停止";
    public string RestartButtonLabel =>
        Status is ServerStatus.Starting or ServerStatus.Stopping ? "再起動中..." : "↻ 再起動";

    public string SettingsSavedMessage
    {
        get => _settingsSavedMessage;
        private set => SetProperty(ref _settingsSavedMessage, value);
    }

    public bool IsVersionsLoading
    {
        get => _isVersionsLoading;
        private set => SetProperty(ref _isVersionsLoading, value);
    }

    public string PermissionsStatusMessage
    {
        get => _permissionsStatusMessage;
        private set => SetProperty(ref _permissionsStatusMessage, value);
    }

    public string LastStartedAtText =>
        _config.LastStartedAt?.ToLocalTime().ToString("yyyy/MM/dd HH:mm") ?? "-";

    // ─── コンソール ─────────────────────────────────────────────
    public string CommandText
    {
        get => _commandText;
        set => SetProperty(ref _commandText, value);
    }

    // ─── 設定 ─────────────────────────────────────────────────
    public bool RestartRequired
    {
        get => _restartRequired;
        private set => SetProperty(ref _restartRequired, value);
    }

    public string JavaPath
    {
        get => _javaPath;
        set
        {
            if (SetProperty(ref _javaPath, value))
            {
                _config.JavaPath = value;
                if (!_isInitializing)
                    _services.Configs.Save(_config);
            }
        }
    }

    // ─── バージョン ────────────────────────────────────────────
    public VersionFilterOption? SelectedVersionFilter
    {
        get => _selectedVersionFilter;
        set
        {
            if (SetProperty(ref _selectedVersionFilter, value))
                ApplyVersionFilter();
        }
    }

    public string VersionFilterSummary
    {
        get => _versionFilterSummary;
        private set => SetProperty(ref _versionFilterSummary, value);
    }

    public MinecraftVersionInfo? SelectedVersion
    {
        get => _selectedVersion;
        set
        {
            if (SetProperty(ref _selectedVersion, value))
            {
                ChangeVersionCommand.RaiseCanExecuteChanged();
                OnPropertyChanged(nameof(SelectedVersionInfo));
            }
        }
    }

    public string SelectedVersionInfo
    {
        get
        {
            if (SelectedVersion is null)
                return "バージョン未選択";

            var typeLabel = string.Equals(SelectedVersion.Type, "release", StringComparison.OrdinalIgnoreCase)
                ? "正規版"
                : string.Equals(SelectedVersion.Type, "snapshot", StringComparison.OrdinalIgnoreCase)
                    ? "スナップショット"
                    : SelectedVersion.Type;

            return $"{typeLabel} / {SelectedVersion.ReleaseTime.ToLocalTime():yyyy/MM/dd HH:mm}";
        }
    }

    // ─── 起動オプション ──────────────────────────────────────────
    public LaunchModeOption? SelectedLaunchMode
    {
        get => _selectedLaunchMode;
        set
        {
            if (SetProperty(ref _selectedLaunchMode, value) && value is not null)
            {
                _config.LaunchModeOverride = value.Id;
                _services.Configs.Save(_config);
                OnPropertyChanged(nameof(LaunchModeDescription));
            }
        }
    }

    public StartupPresetOption? SelectedStartupPreset
    {
        get => _selectedStartupPreset;
        set
        {
            if (SetProperty(ref _selectedStartupPreset, value))
                ApplyStartupPresetCommand?.RaiseCanExecuteChanged();
        }
    }

    public string LaunchModeDescription
    {
        get
        {
            if (!string.Equals(ServerType, "Forge", StringComparison.OrdinalIgnoreCase))
                return "Forge 以外では通常は Auto / server.jar 固定を推奨します。";
            return "Forge は Auto 推奨（run.bat / win_args.txt / server.jar を順に判定）。";
        }
    }

    public string JavaExtraArguments
    {
        get => _javaExtraArguments;
        set
        {
            if (SetProperty(ref _javaExtraArguments, value))
            {
                _config.JavaExtraArguments = value?.Trim() ?? string.Empty;
                _services.Configs.Save(_config);
            }
        }
    }

    public int MemoryXmsMb
    {
        get => _memoryXmsMb;
        set
        {
            var next = Math.Clamp(value, 256, 131072);
            if (next > MemoryXmxMb)
                next = MemoryXmxMb;
            if (SetProperty(ref _memoryXmsMb, next))
            {
                _config.MemoryXmsMb = next;
                _services.Configs.Save(_config);
            }
        }
    }

    public int MemoryXmxMb
    {
        get => _memoryXmxMb;
        set
        {
            var next = Math.Clamp(value, 256, 131072);
            if (next < MemoryXmsMb)
                next = MemoryXmsMb;
            if (SetProperty(ref _memoryXmxMb, next))
            {
                _config.MemoryXmxMb = next;
                _services.Configs.Save(_config);
                OnPropertyChanged(nameof(MemoryUsageDisplayText));
                OnPropertyChanged(nameof(MemoryUsagePercent));
            }
        }
    }

    // ─── 自動再起動 ──────────────────────────────────────────────
    public bool AutoRestartOnCrash
    {
        get => _autoRestartOnCrash;
        set
        {
            if (SetProperty(ref _autoRestartOnCrash, value))
            {
                _config.AutoRestartOnCrash = value;
                if (!_isInitializing)
                    _services.Configs.Save(_config);
            }
        }
    }

    public int AutoRestartDelaySeconds
    {
        get => _autoRestartDelaySeconds;
        set
        {
            var next = Math.Clamp(value, 1, 300);
            if (SetProperty(ref _autoRestartDelaySeconds, next))
            {
                _config.AutoRestartDelaySeconds = next;
                if (!_isInitializing)
                    _services.Configs.Save(_config);
            }
        }
    }

    // ─── パーミッション ─────────────────────────────────────────
    public OpEntry? SelectedOp
    {
        get => _selectedOp;
        set
        {
            if (SetProperty(ref _selectedOp, value))
                RemoveOpCommand.RaiseCanExecuteChanged();
        }
    }

    public WhitelistEntry? SelectedWhitelist
    {
        get => _selectedWhitelist;
        set
        {
            if (SetProperty(ref _selectedWhitelist, value))
                RemoveWhitelistCommand.RaiseCanExecuteChanged();
        }
    }

    public string NewOpName
    {
        get => _newOpName;
        set
        {
            if (SetProperty(ref _newOpName, value))
                AddOpCommand.RaiseCanExecuteChanged();
        }
    }

    public string NewWhitelistName
    {
        get => _newWhitelistName;
        set
        {
            if (SetProperty(ref _newWhitelistName, value))
                AddWhitelistCommand.RaiseCanExecuteChanged();
        }
    }

    // ─── 稼働モニター ─────────────────────────────────────────────
    public double CpuUsagePercent
    {
        get => _cpuUsagePercent;
        private set
        {
            if (SetProperty(ref _cpuUsagePercent, value))
                OnPropertyChanged(nameof(CpuUsageDisplayText));
        }
    }

    public double MemoryUsageMb
    {
        get => _memoryUsageMb;
        private set
        {
            if (SetProperty(ref _memoryUsageMb, value))
            {
                OnPropertyChanged(nameof(MemoryUsageDisplayText));
                OnPropertyChanged(nameof(MemoryUsagePercent));
            }
        }
    }

    public double GpuUsagePercent
    {
        get => _gpuUsagePercent;
        private set
        {
            if (SetProperty(ref _gpuUsagePercent, value))
                OnPropertyChanged(nameof(GpuUsageDisplayText));
        }
    }

    public int OnlinePlayers
    {
        get => _onlinePlayers;
        private set
        {
            if (SetProperty(ref _onlinePlayers, value))
                OnPropertyChanged(nameof(PlayerCountText));
        }
    }

    public string PlayerCountText => $"{OnlinePlayers}/{MaxPlayers}";
    public string CpuUsageDisplayText => $"{CpuUsagePercent:F0}%";
    public string MemoryUsageDisplayText => $"{MemoryUsageMb:F0} MB / {MemoryXmxMb} MB";
    public double MemoryUsagePercent =>
        MemoryXmxMb <= 0 ? 0 : Math.Clamp(MemoryUsageMb / MemoryXmxMb * 100, 0, 100);
    public bool IsGpuMonitoringAvailable => _gpuMonitoringAvailable;
    public string GpuUsageDisplayText => IsGpuMonitoringAvailable ? $"{GpuUsagePercent:F0}%" : "N/A";
    public string GpuMonitorHint =>
        IsGpuMonitoringAvailable
            ? "GPUエンジン使用率（サーバープロセス）"
            : "この環境ではGPU使用率を取得できません。";

    // ─── コマンド ─────────────────────────────────────────────
    public AsyncRelayCommand StartCommand { get; }
    public AsyncRelayCommand StopCommand { get; }
    public AsyncRelayCommand RestartCommand { get; }
    public RelayCommand SendCommandCommand { get; }
    public RelayCommand ExportLogsCommand { get; }
    public RelayCommand SaveSettingsCommand { get; }
    public RelayCommand BrowseJavaCommand { get; }
    public RelayCommand DetectJavaCommand { get; }
    public RelayCommand OpenServerDirectoryCommand { get; }
    public RelayCommand OpenLogsDirectoryCommand { get; }
    public RelayCommand OpenCrashReportsCommand { get; }
    public AsyncRelayCommand LoadVersionsCommand { get; }
    public AsyncRelayCommand ChangeVersionCommand { get; }
    public AsyncRelayCommand RedownloadJarCommand { get; }
    public RelayCommand AddOpCommand { get; }
    public RelayCommand RemoveOpCommand { get; }
    public RelayCommand AddWhitelistCommand { get; }
    public RelayCommand RemoveWhitelistCommand { get; }
    public RelayCommand ReloadPermissionsCommand { get; }
    public RelayCommand ApplyStartupPresetCommand { get; }
    public RelayCommand RunHealthCheckCommand { get; }

    // ─── 設定ロード ────────────────────────────────────────────
    private void LoadSettings()
    {
        var properties = _services.Properties.Load(ServerDirectory, _config);
        Settings.Load(properties);
        UpdateRestartRequired();
    }

    private void UpdateRestartRequired()
    {
        RestartRequired = Settings.IsDirty;
    }

    private void InitializeVersionFilters()
    {
        VersionFilters.Clear();
        VersionFilters.Add(new VersionFilterOption("release", "正規版"));
        VersionFilters.Add(new VersionFilterOption("snapshot", "スナップショット"));
        VersionFilters.Add(new VersionFilterOption("all", "すべて"));
        SelectedVersionFilter = VersionFilters.FirstOrDefault();
    }

    private void ApplyVersionFilter()
    {
        var filterId = SelectedVersionFilter?.Id ?? "release";
        IEnumerable<MinecraftVersionInfo> filteredSource = AvailableVersions;

        filteredSource = filterId switch
        {
            "snapshot" => filteredSource.Where(v => string.Equals(v.Type, "snapshot", StringComparison.OrdinalIgnoreCase)),
            "release" => filteredSource.Where(v => string.Equals(v.Type, "release", StringComparison.OrdinalIgnoreCase)),
            _ => filteredSource
        };

        var filteredList = filteredSource.ToList();
        var selectedVersionId = SelectedVersion?.Id;
        FilteredAvailableVersions.Clear();
        foreach (var version in filteredList)
            FilteredAvailableVersions.Add(version);

        SelectedVersion =
            (!string.IsNullOrWhiteSpace(selectedVersionId)
                ? FilteredAvailableVersions.FirstOrDefault(v =>
                    string.Equals(v.Id, selectedVersionId, StringComparison.OrdinalIgnoreCase))
                : null)
            ?? FilteredAvailableVersions.FirstOrDefault(v =>
                string.Equals(v.Id, _config.Version, StringComparison.OrdinalIgnoreCase))
            ?? FilteredAvailableVersions.FirstOrDefault(v => string.Equals(v.Type, "release", StringComparison.OrdinalIgnoreCase))
            ?? FilteredAvailableVersions.FirstOrDefault();

        var modHint = IsModdedServerType(ServerType) ? "（Forge/Fabric は正規版推奨）" : string.Empty;
        VersionFilterSummary = $"表示 {FilteredAvailableVersions.Count} 件 / 全体 {AvailableVersions.Count} 件{modHint}";
    }

    private void InitializeLaunchOptions()
    {
        LaunchModes.Clear();
        LaunchModes.Add(new LaunchModeOption("Auto", "Auto（推奨）"));
        LaunchModes.Add(new LaunchModeOption("ForceServerJar", "server.jar 固定"));
        LaunchModes.Add(new LaunchModeOption("ForceForgeRunBat", "Forge run.bat 固定"));
        LaunchModes.Add(new LaunchModeOption("ForceForgeWinArgs", "Forge win_args.txt 固定"));

        StartupPresets.Clear();
        StartupPresets.Add(new StartupPresetOption("Balanced", "Balanced（推奨）", 2048, 4096, "-XX:+UseG1GC -XX:+ParallelRefProcEnabled"));
        StartupPresets.Add(new StartupPresetOption("MemorySaver", "Memory Saver", 1024, 2048, "-XX:+UseG1GC"));
        StartupPresets.Add(new StartupPresetOption("Throughput", "Throughput", 4096, 8192, "-XX:+UseG1GC -XX:MaxGCPauseMillis=100"));

        _selectedLaunchMode =
            LaunchModes.FirstOrDefault(item =>
                string.Equals(item.Id, _config.LaunchModeOverride, StringComparison.OrdinalIgnoreCase))
            ?? LaunchModes.FirstOrDefault(item =>
                string.Equals(item.Id, "Auto", StringComparison.OrdinalIgnoreCase));
        OnPropertyChanged(nameof(SelectedLaunchMode));
        OnPropertyChanged(nameof(LaunchModeDescription));

        _selectedStartupPreset =
            StartupPresets.FirstOrDefault(item =>
                string.Equals(item.Id, _config.StartupPresetId, StringComparison.OrdinalIgnoreCase))
            ?? StartupPresets.FirstOrDefault(item =>
                string.Equals(item.Id, "Balanced", StringComparison.OrdinalIgnoreCase));
        OnPropertyChanged(nameof(SelectedStartupPreset));
    }

    private void ApplyStartupPreset()
    {
        if (SelectedStartupPreset is null)
            return;

        MemoryXmsMb = SelectedStartupPreset.MemoryXmsMb;
        MemoryXmxMb = SelectedStartupPreset.MemoryXmxMb;
        JavaExtraArguments = SelectedStartupPreset.JavaExtraArguments;
        _config.StartupPresetId = SelectedStartupPreset.Id;
        _services.Configs.Save(_config);
        _services.Dialog.Show(
            $"起動プリセット「{SelectedStartupPreset.Label}」を適用しました。",
            "完了", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    // ─── Start / Stop / Restart ──────────────────────────────────
    private async Task StartAsync()
    {
        if (Status != ServerStatus.Stopped)
            return;

        RunHealthCheck(showEvenIfCompleted: false);
        WarnIfJavaVersionMismatch();

        if (!Directory.Exists(ServerDirectory))
        {
            _services.Dialog.Show(
                "サーバーディレクトリが見つかりません。",
                "エラー", MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }

        if (!Network.EnsurePortAvailable())
            return;

        await Network.TryOpenPortAsync();

        try
        {
            await _services.Runtime.StartAsync(_config, ServerDirectory);
            if (_runtime.Status != ServerStatus.Stopped)
            {
                _config.LastStartedAt = DateTime.UtcNow;
                _services.Configs.Save(_config);
                OnPropertyChanged(nameof(LastStartedAtText));
            }
        }
        catch (Exception ex)
        {
            _services.Dialog.Show(
                $"起動に失敗しました: {ex.Message}",
                "エラー", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private async Task StopAsync()
    {
        try
        {
            await _services.Runtime.StopAsync(_config);
        }
        catch (Exception ex)
        {
            _services.Dialog.Show(
                $"停止に失敗しました: {ex.Message}",
                "エラー", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            await Network.TryClosePortAsync();
        }
    }

    private async Task RestartAsync()
    {
        try
        {
            await _services.Runtime.RestartAsync(_config, ServerDirectory);
        }
        catch (Exception ex)
        {
            _services.Dialog.Show(
                $"再起動に失敗しました: {ex.Message}",
                "エラー", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    // ─── コンソール ──────────────────────────────────────────────
    private void SendCommand()
    {
        var text = CommandText?.Trim();
        if (string.IsNullOrWhiteSpace(text))
            return;
        _services.Runtime.SendCommand(_config, text);
        CommandText = string.Empty;
    }

    private void ExportLogs()
    {
        try
        {
            var logsDir = Path.Combine(ServerDirectory, "logs");
            Directory.CreateDirectory(logsDir);
            var fileName = $"console-{DateTime.Now:yyyyMMdd-HHmmss}.txt";
            var path = Path.Combine(logsDir, fileName);
            File.WriteAllLines(path, Logs, Encoding.UTF8);
            _services.Dialog.Show(
                $"ログを保存しました: {path}",
                "完了", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            _services.Dialog.Show(
                $"ログ出力に失敗しました: {ex.Message}",
                "エラー", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    // ─── 設定保存 ─────────────────────────────────────────────
    private void SaveSettings()
    {
        try
        {
            var props = Settings.ToModel();
            _services.Properties.Save(ServerDirectory, props);
            _config.Port = props.ServerPort;
            _config.MaxPlayers = props.MaxPlayers;
            _config.Motd = props.Motd;
            _config.OnlineMode = props.OnlineMode;
            _config.EnableCommandBlock = props.EnableCommandBlock;
            _config.Difficulty = props.Difficulty;
            _config.GameMode = props.GameMode;
            _config.Pvp = props.Pvp;
            _config.ViewDistance = props.ViewDistance;
            _config.SpawnProtection = props.SpawnProtection;
            _config.WorldName = props.LevelName;
            _config.Seed = props.Seed;
            _services.Configs.Save(_config);
            Settings.Load(props);
            UpdateRestartRequired();
            OnPropertyChanged(nameof(Port));
            OnPropertyChanged(nameof(MaxPlayers));
            OnPropertyChanged(nameof(PlayerCountText));
            ShowSettingsSaved();
        }
        catch (Exception ex)
        {
            _services.Dialog.Show(
                $"設定保存に失敗しました: {ex.Message}",
                "エラー", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void ShowSettingsSaved()
    {
        SettingsSavedMessage = "✓ 設定を保存しました";
        var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };
        timer.Tick += (_, _) =>
        {
            SettingsSavedMessage = string.Empty;
            timer.Stop();
        };
        timer.Start();
    }

    // ─── Java ────────────────────────────────────────────────
    private void BrowseJava()
    {
        var dialog = new OpenFileDialog { Filter = "java.exe|java.exe", FileName = "java.exe" };
        if (dialog.ShowDialog() == true)
            JavaPath = dialog.FileName;
    }

    private void DetectJava()
    {
        var detected = _services.Java.FindJavaExecutable();
        if (!string.IsNullOrWhiteSpace(detected))
        {
            JavaPath = detected;
        }
        else
        {
            _services.Dialog.Show(
                "Javaが見つかりませんでした。\n\nhttps://adoptium.net から Eclipse Temurin (LTS) をインストールしてください。\nインストール時に「PATH に追加」にチェックを入れてから再度お試しください。",
                "Java 自動検出", MessageBoxButton.OK, MessageBoxImage.Information);
        }
    }

    // ─── バージョン管理 ──────────────────────────────────────────
    private async Task LoadVersionsAsync(bool forceRefresh = false)
    {
        IsVersionsLoading = true;
        try
        {
            var versions = await _services.Versions.GetVersionsAsync(forceRefresh);
            WpfApplication.Current.Dispatcher.Invoke(() =>
            {
                AvailableVersions.Clear();
                foreach (var version in versions)
                    AvailableVersions.Add(version);
                ApplyVersionFilter();
            });
        }
        catch (Exception ex)
        {
            _services.Dialog.Show(
                $"バージョン取得に失敗しました: {ex.Message}",
                "エラー", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            IsVersionsLoading = false;
        }
    }

    private async Task ChangeVersionAsync()
    {
        if (SelectedVersion is null)
            return;

        if (Status != ServerStatus.Stopped)
        {
            _services.Dialog.Show(
                "停止中のみ変更できます。",
                "確認", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        if (IsModdedServerType(ServerType)
            && !string.Equals(SelectedVersion.Type, "release", StringComparison.OrdinalIgnoreCase))
        {
            var modWarningResult = _services.Dialog.Show(
                $"選択中の {SelectedVersion.Id} は {SelectedVersion.Type} です。Forge/Fabric では動作しない可能性があります。続行しますか？",
                "バージョン確認", MessageBoxButton.YesNo, MessageBoxImage.Warning);
            if (modWarningResult != MessageBoxResult.Yes)
                return;
        }

        if (_services.Dialog.Show(
                $"バージョンを {SelectedVersion.Id} に変更します。",
                "確認", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
        {
            return;
        }

        try
        {
            var jarPath = Path.Combine(ServerDirectory, "server.jar");
            await _services.Jars.DownloadAsync(_config.Type, SelectedVersion.Id, jarPath, _config.JavaPath);
            _config.Version = SelectedVersion.Id;
            _services.Configs.Save(_config);
            OnPropertyChanged(nameof(Version));
        }
        catch (Exception ex)
        {
            _services.Dialog.Show(
                $"バージョン変更に失敗しました: {ex.Message}",
                "エラー", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private async Task RedownloadJarAsync()
    {
        if (Status != ServerStatus.Stopped)
        {
            _services.Dialog.Show(
                "停止中のみ再ダウンロードできます。",
                "確認", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        try
        {
            var jarPath = Path.Combine(ServerDirectory, "server.jar");
            await _services.Jars.DownloadAsync(_config.Type, _config.Version, jarPath, _config.JavaPath);
        }
        catch (Exception ex)
        {
            _services.Dialog.Show(
                $"再ダウンロードに失敗しました: {ex.Message}",
                "エラー", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    // ─── パーミッション ─────────────────────────────────────────
    private void LoadPermissions()
    {
        Ops.Clear();
        foreach (var op in _services.Permissions.LoadOps(ServerDirectory))
            Ops.Add(op);

        Whitelist.Clear();
        foreach (var entry in _services.Permissions.LoadWhitelist(ServerDirectory))
            Whitelist.Add(entry);
    }

    private void AddOp()
    {
        var name = NewOpName.Trim();
        if (string.IsNullOrWhiteSpace(name))
            return;
        if (Ops.Any(o => string.Equals(o.Name, name, StringComparison.OrdinalIgnoreCase)))
            return;
        Ops.Add(new OpEntry { Name = name });
        _services.Permissions.SaveOps(ServerDirectory, Ops);
        NewOpName = string.Empty;
        PermissionsStatusMessage = $"✓ OP「{name}」を追加しました";
    }

    private void RemoveOp()
    {
        if (SelectedOp is null)
            return;
        var name = SelectedOp.Name;
        Ops.Remove(SelectedOp);
        _services.Permissions.SaveOps(ServerDirectory, Ops);
        PermissionsStatusMessage = $"✓ OP「{name}」を削除しました";
    }

    private void AddWhitelist()
    {
        var name = NewWhitelistName.Trim();
        if (string.IsNullOrWhiteSpace(name))
            return;
        if (Whitelist.Any(o => string.Equals(o.Name, name, StringComparison.OrdinalIgnoreCase)))
            return;
        Whitelist.Add(new WhitelistEntry { Name = name });
        _services.Permissions.SaveWhitelist(ServerDirectory, Whitelist);
        NewWhitelistName = string.Empty;
        PermissionsStatusMessage = $"✓ ホワイトリスト「{name}」を追加しました";
    }

    private void RemoveWhitelist()
    {
        if (SelectedWhitelist is null)
            return;
        var name = SelectedWhitelist.Name;
        Whitelist.Remove(SelectedWhitelist);
        _services.Permissions.SaveWhitelist(ServerDirectory, Whitelist);
        PermissionsStatusMessage = $"✓ ホワイトリスト「{name}」を削除しました";
    }

    // ─── フォルダ操作 ────────────────────────────────────────────
    private void OpenServerDirectory() =>
        OpenDirectory(ServerDirectory, "サーバーディレクトリが見つかりません。");

    private void OpenLogsDirectory() =>
        OpenDirectory(Path.Combine(ServerDirectory, "logs"), "ログフォルダが見つかりません。");

    private void OpenCrashReports() =>
        OpenDirectory(Path.Combine(ServerDirectory, "crash-reports"), "クラッシュレポートが見つかりません。");

    private void OpenDirectory(string path, string missingMessage)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(path) || !Directory.Exists(path))
            {
                _services.Dialog.Show(missingMessage, "エラー", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }
            Process.Start(new ProcessStartInfo { FileName = path, UseShellExecute = true });
        }
        catch (Exception ex)
        {
            _services.Dialog.Show(
                $"フォルダを開けませんでした: {ex.Message}",
                "エラー", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    // ─── ヘルスチェック ──────────────────────────────────────────
    private void RunHealthCheck(bool showEvenIfCompleted)
    {
        if (!showEvenIfCompleted && _config.HasCompletedInitialHealthCheck)
            return;

        var issues = new List<string>();
        var fixes = new List<string>();

        if (string.Equals(ServerType, "Paper", StringComparison.OrdinalIgnoreCase))
        {
            var pluginsDir = Path.Combine(ServerDirectory, "plugins");
            if (!Directory.Exists(pluginsDir))
            {
                Directory.CreateDirectory(pluginsDir);
                fixes.Add("plugins フォルダを作成しました。");
            }
        }

        if (string.Equals(ServerType, "Fabric", StringComparison.OrdinalIgnoreCase))
        {
            var modsDir = Path.Combine(ServerDirectory, "mods");
            if (!Directory.Exists(modsDir))
            {
                Directory.CreateDirectory(modsDir);
                fixes.Add("mods フォルダを作成しました。");
            }
        }

        if (string.Equals(ServerType, "Forge", StringComparison.OrdinalIgnoreCase))
        {
            var runBat = Path.Combine(ServerDirectory, "run.bat");
            var hasRunBat = File.Exists(runBat);
            var hasWinArgs =
                Directory.Exists(ServerDirectory)
                && Directory
                    .EnumerateFiles(ServerDirectory, "win_args.txt", SearchOption.AllDirectories)
                    .Any();
            if (!hasRunBat && !hasWinArgs)
            {
                issues.Add(
                    "Forge の起動補助ファイル (run.bat / win_args.txt) が見つかりません。\nバージョン再ダウンロードまたは Forge の再インストールを推奨します。"
                );
            }
        }

        if (IsModdedServerType(ServerType))
        {
            var versionType = AvailableVersions
                .FirstOrDefault(v => string.Equals(v.Id, _config.Version, StringComparison.OrdinalIgnoreCase))
                ?.Type;
            if (!string.IsNullOrWhiteSpace(versionType)
                && !string.Equals(versionType, "release", StringComparison.OrdinalIgnoreCase))
            {
                issues.Add(
                    $"現在のバージョン {_config.Version} は {versionType} です。Forge/Fabric では起動やMODの読み込みに失敗する場合があります。正規版を推奨します。"
                );
            }
        }

        _config.HasCompletedInitialHealthCheck = true;
        _services.Configs.Save(_config);

        if (issues.Count == 0 && fixes.Count == 0)
        {
            if (showEvenIfCompleted)
            {
                _services.Dialog.Show(
                    "ヘルスチェックで問題は見つかりませんでした。",
                    "ヘルスチェック", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            return;
        }

        var message = string.Join(Environment.NewLine + Environment.NewLine, issues.Concat(fixes));
        var image = issues.Count > 0 ? MessageBoxImage.Warning : MessageBoxImage.Information;
        _services.Dialog.Show(message, "初回ヘルスチェック", MessageBoxButton.OK, image);
    }

    private void WarnIfJavaVersionMismatch()
    {
        var requiredMajor = _services.Java.GetRequiredJavaMajor(_config.Version);
        if (requiredMajor is null)
            return;

        var javaExe = string.IsNullOrWhiteSpace(JavaPath)
            ? _services.Java.FindJavaExecutable() ?? "java"
            : JavaPath;

        if (!_services.Java.TryGetJavaMajorVersion(javaExe, out var actualMajor, out var rawVersion))
            return;

        if (actualMajor >= requiredMajor)
            return;

        var versionText = string.IsNullOrWhiteSpace(rawVersion)
            ? actualMajor.ToString()
            : rawVersion;
        _services.Dialog.Show(
            $"Minecraft {Version} は Java {requiredMajor} 以上が推奨です。現在の Java: {versionText}\n必要に応じて「設定」タブで java.exe を切り替えてください。",
            "Java バージョン警告", MessageBoxButton.OK, MessageBoxImage.Warning);
    }

    // ─── 稼働モニター ────────────────────────────────────────────
    private void UpdateStats()
    {
        var process = _runtime.Process;
        if (process is null || process.HasExited || Status != ServerStatus.Running)
        {
            CpuUsagePercent = 0;
            MemoryUsageMb = 0;
            GpuUsagePercent = 0;
            _lastCpuCheck = DateTime.MinValue;
            _lastCpuTime = TimeSpan.Zero;
            _lastGpuCheck = DateTime.MinValue;
            return;
        }

        try
        {
            process.Refresh();
            var now = DateTime.UtcNow;
            var totalCpu = process.TotalProcessorTime;
            if (_lastCpuCheck != DateTime.MinValue)
            {
                var deltaCpu = totalCpu - _lastCpuTime;
                var deltaTime = now - _lastCpuCheck;
                var cpuPercent =
                    deltaTime.TotalMilliseconds > 0
                        ? deltaCpu.TotalMilliseconds
                            / (deltaTime.TotalMilliseconds * Environment.ProcessorCount)
                            * 100
                        : 0;
                CpuUsagePercent = Math.Clamp(cpuPercent, 0, 100);
            }
            _lastCpuCheck = now;
            _lastCpuTime = totalCpu;
            MemoryUsageMb = process.WorkingSet64 / (1024.0 * 1024.0);
            UpdateGpuUsage(process.Id, now);
        }
        catch
        {
            // Ignore sampling errors.
        }
    }

    private void UpdateGpuUsage(int processId, DateTime now)
    {
        if (!_gpuMonitoringAvailable)
            return;
        if (_lastGpuCheck != DateTime.MinValue && (now - _lastGpuCheck) < GpuSampleInterval)
            return;

        _lastGpuCheck = now;
        try
        {
            var sampled = SampleGpuUsagePercent(processId);
            GpuUsagePercent = Math.Clamp(sampled, 0, 100);
        }
        catch (ManagementException) { DisableGpuMonitoring(); }
        catch (InvalidOperationException) { DisableGpuMonitoring(); }
        catch
        {
            // Ignore transient sampling errors.
        }
    }

    private void DisableGpuMonitoring()
    {
        if (!_gpuMonitoringAvailable)
            return;
        _gpuMonitoringAvailable = false;
        GpuUsagePercent = 0;
        OnPropertyChanged(nameof(IsGpuMonitoringAvailable));
        OnPropertyChanged(nameof(GpuUsageDisplayText));
        OnPropertyChanged(nameof(GpuMonitorHint));
    }

    private static double SampleGpuUsagePercent(int processId)
    {
        var instanceToken = $"pid_{processId}_";
        var totalPercent = 0.0;
        using var searcher = new ManagementObjectSearcher(
            @"root\CIMV2",
            "SELECT Name, UtilizationPercentage FROM Win32_PerfFormattedData_GPUPerformanceCounters_GPUEngine"
        );
        using var results = searcher.Get();
        foreach (ManagementObject gpuEngine in results)
        {
            var name = gpuEngine["Name"]?.ToString();
            if (string.IsNullOrWhiteSpace(name)
                || name.IndexOf(instanceToken, StringComparison.OrdinalIgnoreCase) < 0)
            {
                continue;
            }

            var raw = gpuEngine["UtilizationPercentage"];
            totalPercent += raw switch
            {
                byte value => value,
                ushort value => value,
                uint value => value,
                ulong value => value,
                sbyte value => value,
                short value => value,
                int value => value,
                long value => value,
                float value => value,
                double value => value,
                decimal value => (double)value,
                _ => 0
            };
        }
        return totalPercent;
    }

    // ─── イベントハンドラ ────────────────────────────────────────
    private void OnStatusChanged(ServerStatus status)
    {
        Status = status;
    }

    private void OnLogReceived(string message)
    {
        var match = PlayerCountRegex.Match(message);
        if (match.Success && int.TryParse(match.Groups[1].Value, out var count))
            OnlinePlayers = count;
    }

    private static bool IsModdedServerType(string? serverType) =>
        string.Equals(serverType, "Forge", StringComparison.OrdinalIgnoreCase)
        || string.Equals(serverType, "Fabric", StringComparison.OrdinalIgnoreCase);

    // ─── Dispose ─────────────────────────────────────────────
    public void Dispose()
    {
        if (_isDisposed)
            return;
        _isDisposed = true;
        _runtime.StatusChanged -= OnStatusChanged;
        _runtime.LogReceived -= OnLogReceived;
        _statsTimer.Stop();
        GC.SuppressFinalize(this);
    }
}
