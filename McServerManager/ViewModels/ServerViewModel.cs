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
    private static readonly Regex BedrockPlayerConnectedRegex = new(
        @"Player connected: (?<name>[^,]+),",
        RegexOptions.Compiled
    );
    private static readonly Regex BedrockPlayerDisconnectedRegex = new(
        @"Player disconnected: (?<name>[^,]+),",
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
    private string _currentView = "overview";
    private string _javaWarning = string.Empty;

    private readonly IBackupSchedulerService _backupScheduler;
    private readonly HashSet<string> _bedrockOnlinePlayers = new(StringComparer.OrdinalIgnoreCase);

    public ServerViewModel(
        AppServices services,
        IBackupSchedulerService backupScheduler,
        IBedrockServerService bedrockServer,
        IBedrockPropertiesService bedrockProperties,
        IPortForwardingService portForwarding,
        IJavaRuntimeInstaller javaInstaller,
        ServerConfig config)
    {
        _isInitializing = true;
        _services = services;
        JavaSetup = new JavaSetupViewModel(javaInstaller);
        _backupScheduler = backupScheduler;
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
        CrashHistory = [];

        _autoRestartOnCrash = _config.AutoRestartOnCrash;
        _autoRestartDelaySeconds = _config.AutoRestartDelaySeconds;
        _javaPath = _config.JavaPath;
        _memoryXmsMb = _config.MemoryXmsMb;
        _memoryXmxMb = _config.MemoryXmxMb;
        _javaExtraArguments = _config.JavaExtraArguments ?? string.Empty;
        InitializeLaunchOptions();
        InitializeVersionFilters();

        // サブViewModel の初期化
        World = new ServerWorldViewModel(_services, _backupScheduler, _config, _settings, () => Status, bedrockProperties);
        Addon = new ServerAddonViewModel(_services, _config);
        Network = new ServerNetworkViewModel(_services, _config, _appSettings, portForwarding);
        Network.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is nameof(ServerNetworkViewModel.IsFirewallConfigured)
                or nameof(ServerNetworkViewModel.IsUpnpOpen))
            {
                OnPropertyChanged(nameof(LanReady));
                OnPropertyChanged(nameof(PublicReady));
            }
        };
        ResourcePack = new ServerResourcePackViewModel(_services, _config);
        if (IsBedrock)
        {
            Bedrock = new ServerBedrockViewModel(
                bedrockServer, bedrockProperties, _services.Worlds, _services.Configs, _services.Dialog, _services.Network,
                _config, () => Status, command => _services.Runtime.SendCommand(_config, command));
            Bedrock.SettingsSaved += OnBedrockSettingsSaved;
            Bedrock.Update.Updated += OnBedrockUpdated;
            Bedrock.Settings.PropertyChanged += (_, _) => UpdateRestartRequired();
        }

        StartCommand = new AsyncRelayCommand(StartAsync, () => Status == ServerStatus.Stopped && !JavaSetup.IsRunning);
        SetupJavaCommand = new AsyncRelayCommand(SetupJavaFromSettingsAsync, () => !JavaSetup.IsRunning);
        JavaSetup.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName != nameof(JavaSetupViewModel.IsRunning))
                return;
            StartCommand.RaiseCanExecuteChanged();
            SetupJavaCommand.RaiseCanExecuteChanged();
            DetectJavaCommand.RaiseCanExecuteChanged();
        };
        StopCommand = new AsyncRelayCommand(StopAsync, () => Status != ServerStatus.Stopped);
        RestartCommand = new AsyncRelayCommand(RestartAsync, () => Status == ServerStatus.Running);
        SendCommandCommand = new RelayCommand(
            _ => SendCommand(),
            _ => Status == ServerStatus.Running
        );
        ExportLogsCommand = new RelayCommand(_ => ExportLogs());
        SaveSettingsCommand = new RelayCommand(_ => SaveSettings());
        BrowseJavaCommand = new RelayCommand(_ => BrowseJava());
        DetectJavaCommand = new AsyncRelayCommand(DetectJavaAsync, () => !JavaSetup.IsRunning);
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
        SelectViewCommand = new RelayCommand(param => SelectView(param as string));
        ShowAllowlistSettingsCommand = new RelayCommand(_ =>
        {
            SelectView("settings");
            SettingsSectionRequested?.Invoke("allowlist");
        });
        SaveAllCommand = new RelayCommand(_ => SaveAllWorld(), _ => Status == ServerStatus.Running);
        CopyLanAddressCommand = new RelayCommand(_ => CopyLanAddress());
        RefreshCrashHistoryCommand = new RelayCommand(_ => LoadCrashHistory());

        LoadSettings();
        LoadPermissions();
        LoadCrashHistory();
        UpdateJavaWarning();
        if (!IsBedrock)
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
    public ServerResourcePackViewModel ResourcePack { get; }
    /// <summary>統合版サーバーのときのみ生成される（Java 版では null）。</summary>
    public ServerBedrockViewModel? Bedrock { get; }

    // ─── 識別情報 ────────────────────────────────────────────────
    public string Name => _config.Name;
    public string ServerId => _config.ServerId;
    public string ServerType => _config.Type;
    public string Version => _config.Version;
    public int Port => _config.Port;
    public int PortV6 => _config.PortV6;
    public int MaxPlayers => _config.MaxPlayers;
    public string ServerDirectory => _config.DirectoryPath;

    // ─── エディション ────────────────────────────────────────────
    public bool IsBedrock => ServerEditions.IsBedrock(_config);
    public string EditionLabel => ServerEditions.GetEditionLabel(_config.Type);
    public string ServerTypeDisplay => ServerEditions.GetTypeDisplayName(_config.Type);
    /// <summary>リソースパックの HTTP 配信は Java 版のみ（統合版は resource_packs フォルダで配布）。</summary>
    public bool SupportsResourcePack => !IsBedrock;

    // ─── コレクション ─────────────────────────────────────────────
    public ObservableCollection<string> Logs { get; }
    public ObservableCollection<MinecraftVersionInfo> AvailableVersions { get; }
    public ObservableCollection<MinecraftVersionInfo> FilteredAvailableVersions { get; }
    public ObservableCollection<VersionFilterOption> VersionFilters { get; }
    public ObservableCollection<OpEntry> Ops { get; }
    public ObservableCollection<WhitelistEntry> Whitelist { get; }
    public ObservableCollection<LaunchModeOption> LaunchModes { get; }
    public ObservableCollection<StartupPresetOption> StartupPresets { get; }
    public ObservableCollection<CrashEntry> CrashHistory { get; }

    // ─── ナビゲーション(サイドバーから画面切替) ─────────────────────
    public string CurrentView
    {
        get => _currentView;
        set
        {
            if (SetProperty(ref _currentView, value))
            {
                OnPropertyChanged(nameof(CurrentViewLabel));
            }
        }
    }

    public string CurrentViewLabel => _currentView switch
    {
        "overview" => "概要",
        "console" => "コンソール",
        "network" => "接続・公開",
        "world" => "ワールド",
        "backups" => "バックアップ",
        "addons" => AddonNavLabel,
        "respack" => "リソースパック",
        "version" => "バージョン",
        "crashes" => "クラッシュ復旧",
        "settings" => "サーバー設定",
        _ => "概要",
    };

    public bool IsRunning => Status == ServerStatus.Running;
    public bool IsStopped => Status == ServerStatus.Stopped;

    /// <summary>稼働時間(起動時刻からの経過)。1秒ごとの統計タイマーで更新。</summary>
    public string Uptime
    {
        get
        {
            if (!IsRunning || _config.LastStartedAt is null)
                return "—";
            var span = DateTime.UtcNow - _config.LastStartedAt.Value;
            if (span < TimeSpan.Zero) span = TimeSpan.Zero;
            return span.TotalHours >= 1
                ? $"{(int)span.TotalHours}h {span.Minutes:00}m"
                : $"{span.Minutes}m {span.Seconds:00}s";
        }
    }

    /// <summary>LAN 参加可能(起動中 かつ Firewall 構成済み)。</summary>
    public bool LanReady => IsRunning && Network.IsFirewallConfigured;

    /// <summary>外部公開可能(起動中 かつ UPnP 開放済み)。</summary>
    public bool PublicReady => IsRunning && Network.IsUpnpOpen;

    public bool SupportsAddons => Addon.SupportsAddonManagement;
    public string AddonNavLabel => SupportsAddons ? Addon.AddonCategoryName : "アドオン";
    public bool HasPendingChanges => IsBedrock ? Bedrock?.Settings.IsDirty == true : Settings.IsDirty;

    /// <summary>LAN 参加アドレスのホスト部（先頭の LAN IP）。</summary>
    public string LanHost
    {
        get
        {
            var ip = Network.LanIpAddresses.FirstOrDefault();
            return string.IsNullOrWhiteSpace(ip) ? "127.0.0.1" : ip;
        }
    }

    /// <summary>
    /// LAN 参加アドレス。Java 版は「IP:ポート」をそのまま入力できるが、
    /// 統合版はアドレスとポートを別欄に入力するため IP のみ。
    /// </summary>
    public string LanAddress => IsBedrock ? LanHost : $"{LanHost}:{Port}";

    public string JavaWarning
    {
        get => _javaWarning;
        private set
        {
            if (SetProperty(ref _javaWarning, value))
                OnPropertyChanged(nameof(HasJavaWarning));
        }
    }

    public bool HasJavaWarning => !string.IsNullOrWhiteSpace(JavaWarning);

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
                OnPropertyChanged(nameof(IsRunning));
                OnPropertyChanged(nameof(IsStopped));
                OnPropertyChanged(nameof(Uptime));
                OnPropertyChanged(nameof(LanReady));
                OnPropertyChanged(nameof(PublicReady));
                OnPropertyChanged(nameof(CpuUsageDisplayText));
                OnPropertyChanged(nameof(MemoryUsageDisplayText));
                OnPropertyChanged(nameof(GpuUsageDisplayText));
                StartCommand.RaiseCanExecuteChanged();
                StopCommand.RaiseCanExecuteChanged();
                RestartCommand.RaiseCanExecuteChanged();
                SendCommandCommand.RaiseCanExecuteChanged();
                SaveAllCommand.RaiseCanExecuteChanged();
                // サブViewModelに Status 変化を伝播
                World.OnServerStatusChanged();
                Bedrock?.Players.RefreshOperatorCommands();
                if (value == ServerStatus.Stopped)
                {
                    lock (_bedrockOnlinePlayers)
                        _bedrockOnlinePlayers.Clear();
                    OnlinePlayers = 0;
                    LoadCrashHistory();
                }
            }
        }
    }

    public string StatusText =>
        Status switch
        {
            ServerStatus.Starting => "起動中",
            ServerStatus.Running => "稼働中",
            ServerStatus.Stopping => "停止中",
            ServerStatus.Stopped => "停止",
            _ => "停止を確認できません",
        };

    public string StartButtonLabel => Status == ServerStatus.Starting ? "起動中..." : "▶ 起動";
    public string StopButtonLabel => Status switch
    {
        ServerStatus.Stopping => "停止中...",
        ServerStatus.Unknown => "■ 停止を再確認",
        _ => "■ 停止",
    };
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

    /// <summary>
    /// サーバー設定の特定の項目を表示してほしいときに発生する（View がその位置までスクロールする）。
    /// 引数は "java"（Java ランタイム）または "allowlist"（参加許可リスト）。
    /// </summary>
    public event Action<string>? SettingsSectionRequested;

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
                UpdateJavaWarning();
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
    public string CpuUsageDisplayText => IsRunning ? $"{CpuUsagePercent:F0}%" : "—";
    // 統合版は Java のヒープ上限 (Xmx) がないため、使用量のみ表示し、割合は PC の総メモリ比にする
    public string MemoryUsageDisplayText =>
        IsBedrock
            ? (IsRunning ? $"{MemoryUsageMb:F0} MB" : "—")
            : IsRunning ? $"{MemoryUsageMb:F0} MB / {MemoryXmxMb} MB" : $"— / {MemoryXmxMb} MB";
    public double MemoryUsagePercent =>
        IsBedrock
            ? Math.Clamp(MemoryUsageMb * 1024 * 1024 / Math.Max(1, GC.GetGCMemoryInfo().TotalAvailableMemoryBytes) * 100, 0, 100)
            : MemoryXmxMb <= 0 ? 0 : Math.Clamp(MemoryUsageMb / MemoryXmxMb * 100, 0, 100);
    public bool IsGpuMonitoringAvailable => _gpuMonitoringAvailable;
    public string GpuUsageDisplayText =>
        !IsRunning ? "—" : IsGpuMonitoringAvailable ? $"{GpuUsagePercent:F0}%" : "N/A";
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
    public AsyncRelayCommand DetectJavaCommand { get; }
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
    public RelayCommand SelectViewCommand { get; }
    public AsyncRelayCommand SetupJavaCommand { get; }
    public JavaSetupViewModel JavaSetup { get; }
    public RelayCommand ShowAllowlistSettingsCommand { get; }
    public RelayCommand SaveAllCommand { get; }
    public RelayCommand CopyLanAddressCommand { get; }
    public RelayCommand RefreshCrashHistoryCommand { get; }

    // ─── 設定ロード ────────────────────────────────────────────
    private void LoadSettings()
    {
        var properties = _services.Properties.Load(ServerDirectory, _config);
        Settings.Load(properties);
        UpdateRestartRequired();
    }

    private void UpdateRestartRequired()
    {
        RestartRequired = HasPendingChanges;
        OnPropertyChanged(nameof(HasPendingChanges));
    }

    private void OnBedrockUpdated()
    {
        OnPropertyChanged(nameof(Version));
        Bedrock?.LoadSettings();
        Network.NotifyPortChanged();
    }

    private void OnBedrockSettingsSaved()
    {
        UpdateRestartRequired();
        Network.NotifyPortChanged();
        OnPropertyChanged(nameof(Port));
        OnPropertyChanged(nameof(MaxPlayers));
        OnPropertyChanged(nameof(PlayerCountText));
        OnPropertyChanged(nameof(LanAddress));
        World.LoadWorlds();
        LoadSettings();
        ShowSettingsSaved();
    }

    // ─── ナビ・クイックアクション ─────────────────────────────────
    private void SelectView(string? view)
    {
        if (string.IsNullOrWhiteSpace(view))
            return;
        // アドオン非対応サーバーでアドオン画面に来た場合は概要へ戻す
        if ((string.Equals(view, "addons", StringComparison.OrdinalIgnoreCase) && !SupportsAddons)
            || (string.Equals(view, "respack", StringComparison.OrdinalIgnoreCase) && !SupportsResourcePack))
        {
            CurrentView = "overview";
            return;
        }
        // 初回起動でサーバーがワールドを生成するため、開くたびに一覧を読み直す
        if (string.Equals(view, "world", StringComparison.OrdinalIgnoreCase))
            World.LoadWorlds();
        // 参加許可リストの登録など、別画面での変更を警告に反映する
        if (view is "overview" or "network")
            Network.NotifyPortChanged();
        CurrentView = view;
    }

    private void SaveAllWorld()
    {
        if (Status != ServerStatus.Running)
            return;
        _services.Runtime.SendCommand(_config, "save-all");
    }

    private void CopyLanAddress()
    {
        try { System.Windows.Clipboard.SetText(LanAddress); }
        catch { /* クリップボードは稀に失敗する */ }
    }

    // ─── クラッシュ履歴(crash-reports フォルダから読み取り) ──────────
    private void LoadCrashHistory()
    {
        try
        {
            CrashHistory.Clear();
            var dir = Path.Combine(ServerDirectory, "crash-reports");
            if (!Directory.Exists(dir))
                return;

            var files = new DirectoryInfo(dir)
                .EnumerateFiles("*.txt")
                .OrderByDescending(f => f.LastWriteTimeUtc)
                .Take(30);

            foreach (var file in files)
            {
                CrashHistory.Add(new CrashEntry
                {
                    FileName = file.Name,
                    FullPath = file.FullName,
                    When = file.LastWriteTimeUtc,
                    Reason = ReadCrashReason(file.FullName),
                });
            }
        }
        catch
        {
            // クラッシュ履歴の取得失敗は致命的でないため握りつぶす
        }
        OnPropertyChanged(nameof(HasCrashHistory));
        OnPropertyChanged(nameof(CrashHistorySummary));
    }

    public bool HasCrashHistory => CrashHistory.Count > 0;
    public string CrashHistorySummary =>
        CrashHistory.Count == 0 ? "クラッシュ履歴なし" : $"{CrashHistory.Count} 件の記録";

    private static string ReadCrashReason(string path)
    {
        try
        {
            foreach (var line in File.ReadLines(path).Take(40))
            {
                var trimmed = line.Trim();
                if (trimmed.StartsWith("Description:", StringComparison.OrdinalIgnoreCase))
                    return trimmed["Description:".Length..].Trim();
            }
        }
        catch
        {
            // 読み取り失敗時は既定メッセージ
        }
        return "クラッシュレポート";
    }

    private void UpdateJavaWarning()
    {
        if (IsBedrock)
        {
            JavaWarning = string.Empty;
            return;
        }

        try
        {
            var requiredMajor = _services.Java.GetRequiredJavaMajor(_config.Version);
            if (requiredMajor is null)
            {
                JavaWarning = string.Empty;
                return;
            }

            var javaExe = string.IsNullOrWhiteSpace(JavaPath)
                ? _services.Java.FindJavaExecutable() ?? "java"
                : JavaPath;

            if (!_services.Java.TryGetJavaMajorVersion(javaExe, out var actualMajor, out var rawVersion))
            {
                JavaWarning = $"Minecraft {Version} は Java {requiredMajor} 以上を推奨します。Java が検出できませんでした。";
                return;
            }

            JavaWarning = actualMajor >= requiredMajor
                ? string.Empty
                : $"Minecraft {Version} は Java {requiredMajor} 以上を推奨します。現在: Java {rawVersion ?? actualMajor.ToString()}";
        }
        catch
        {
            JavaWarning = string.Empty;
        }
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

        if (!await EnsureJavaAvailableAsync())
            return;

        RunHealthCheck(showEvenIfCompleted: false);
        await OfferJavaUpgradeIfTooOldAsync();

        if (!Directory.Exists(ServerDirectory))
        {
            _services.Dialog.Show(
                "サーバーディレクトリが見つかりません。",
                "エラー", MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }

        if (!Network.EnsurePortAvailable())
            return;

        Bedrock?.ResetSignalingStatus();

        await Network.TryOpenPortAsync();

        try
        {
            await _services.Runtime.StartAsync(_config, ServerDirectory);
            if (_runtime.Status is ServerStatus.Starting or ServerStatus.Running)
            {
                _config.LastStartedAt = DateTime.UtcNow;
                _services.Configs.Save(_config);
                OnPropertyChanged(nameof(LastStartedAtText));
            }
        }
        catch (JavaNotFoundException ex)
        {
            await Network.TryClosePortAsync();
            if (await OfferJavaSetupAsync($"Java を起動できませんでした。\n{ex.JavaPath}"))
            {
                _services.Dialog.Show("Java を用意しました。もう一度「起動」を押してください。",
                    "Java の自動セットアップ", MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }
        catch (Exception ex)
        {
            _services.Dialog.Show(
                $"起動に失敗しました: {ex.Message}",
                "エラー", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    /// <summary>
    /// Java 版の起動前に java.exe を解決する。未設定なら自動検出（MaiPilot が導入した Java を含む）して設定する。
    /// 見つからなければ自動セットアップを提案し、断られたら「サーバー設定」の Java ランタイムへ案内して false を返す。
    /// </summary>
    private async Task<bool> EnsureJavaAvailableAsync()
    {
        if (IsBedrock)
            return true;

        if (!string.IsNullOrWhiteSpace(JavaPath))
        {
            // "java" のようなコマンド名は PATH 解決に任せ、失敗したら起動時の例外で案内する
            if (!Path.IsPathRooted(JavaPath) || File.Exists(JavaPath))
                return true;

            return await OfferJavaSetupAsync($"設定されている Java が見つかりません。\n{JavaPath}");
        }

        var detected = _services.Java.FindJavaExecutable();
        var managed = JavaSetup.FindInstalled(RequiredJavaInstallMajor);
        // 検出した Java が古く、MaiPilot が導入した適切な版があればそちらを使う
        if (managed is not null && (detected is null || !IsJavaNewEnough(detected)))
        {
            JavaPath = managed;
            return true;
        }

        if (!string.IsNullOrWhiteSpace(detected))
        {
            JavaPath = detected;
            return true;
        }

        return await OfferJavaSetupAsync("Java が見つからないため起動できません。");
    }

    /// <summary>このサーバーの Minecraft に合わせて自動セットアップする Java のメジャー版。</summary>
    private int RequiredJavaInstallMajor => JavaSetup.GetInstallMajor(_services.Java.GetRequiredJavaMajor(_config.Version));

    private bool IsJavaNewEnough(string javaExe)
    {
        var required = _services.Java.GetRequiredJavaMajor(_config.Version);
        return required is null
               || (_services.Java.TryGetJavaMajorVersion(javaExe, out var actual, out _) && actual >= required);
    }

    /// <summary>
    /// Java の自動セットアップを提案する。「はい」なら導入してこのサーバーの Java に設定し true を返す。
    /// 「いいえ」または失敗時は「サーバー設定」の Java ランタイムへ案内して false を返す。
    /// </summary>
    private async Task<bool> OfferJavaSetupAsync(string reason)
    {
        var major = RequiredJavaInstallMajor;
        var answer = _services.Dialog.Show(
            $"{reason}\n\nJava {major}（Eclipse Temurin）を自動でセットアップしますか？\n" +
            "・約 50 MB をダウンロードします（管理者権限は不要です）\n" +
            "・MaiPilot 専用のフォルダに入れ、このサーバーの Java に設定します\n\n" +
            "「いいえ」を選ぶと、Java を自分で指定する画面を開きます。",
            "Java のセットアップ", MessageBoxButton.YesNo, MessageBoxImage.Question);
        if (answer != MessageBoxResult.Yes)
        {
            ShowJavaSettings();
            return false;
        }

        return await RunJavaSetupAsync(major);
    }

    /// <summary>Java が古いとき（例: 26.x に Java 21）、必要な版の自動セットアップを提案する。断られたらそのまま起動する。</summary>
    private async Task OfferJavaUpgradeIfTooOldAsync()
    {
        if (IsBedrock)
            return;

        var requiredMajor = _services.Java.GetRequiredJavaMajor(_config.Version);
        if (requiredMajor is null)
            return;

        var javaExe = string.IsNullOrWhiteSpace(JavaPath)
            ? _services.Java.FindJavaExecutable() ?? "java"
            : JavaPath;
        if (!_services.Java.TryGetJavaMajorVersion(javaExe, out var actualMajor, out var rawVersion) || actualMajor >= requiredMajor)
            return;

        var major = RequiredJavaInstallMajor;
        var answer = _services.Dialog.Show(
            $"Minecraft {Version} には Java {requiredMajor} 以上が必要です。現在の Java: {rawVersion ?? actualMajor.ToString()}\n\n" +
            $"Java {major}（Eclipse Temurin）を自動でセットアップして、このサーバーに使いますか？（約 50 MB・管理者権限不要）\n" +
            "「いいえ」を選ぶと、今の Java のまま起動します。",
            "Java のバージョン", MessageBoxButton.YesNo, MessageBoxImage.Warning);
        if (answer == MessageBoxResult.Yes)
            await RunJavaSetupAsync(major);
    }

    private async Task<bool> RunJavaSetupAsync(int major)
    {
        try
        {
            JavaPath = await JavaSetup.RunAsync(major);
            return true;
        }
        catch (Exception ex)
        {
            _services.Dialog.Show(
                $"Java のセットアップに失敗しました: {ex.Message}\n\nインターネット接続を確認してもう一度試すか、Java を自分で指定してください。",
                "Java のセットアップ", MessageBoxButton.OK, MessageBoxImage.Error);
            ShowJavaSettings();
            return false;
        }
    }

    /// <summary>「サーバー設定」の Java ランタイムの自動セットアップボタンから呼ぶ。</summary>
    private async Task SetupJavaFromSettingsAsync()
    {
        var major = RequiredJavaInstallMajor;
        if (await RunJavaSetupAsync(major))
        {
            _services.Dialog.Show($"Java {major} をセットアップし、このサーバーの Java に設定しました。",
                "Java のセットアップ", MessageBoxButton.OK, MessageBoxImage.Information);
        }
    }

    private void ShowJavaSettings()
    {
        SelectView("settings");
        SettingsSectionRequested?.Invoke("java");
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
        catch (JavaNotFoundException ex)
        {
            if (await OfferJavaSetupAsync($"Java を起動できませんでした。\n{ex.JavaPath}"))
            {
                _services.Dialog.Show("Java を用意しました。もう一度「起動」を押してください。",
                    "Java の自動セットアップ", MessageBoxButton.OK, MessageBoxImage.Information);
            }
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
        if (Bedrock is not null)
        {
            try
            {
                Bedrock.SaveSettings();
            }
            catch (Exception ex)
            {
                _services.Dialog.Show(
                    $"設定保存に失敗しました: {ex.Message}",
                    "エラー", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            return;
        }

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

    private async Task DetectJavaAsync()
    {
        var detected = _services.Java.FindJavaExecutable() ?? JavaSetup.FindInstalled(RequiredJavaInstallMajor);
        if (!string.IsNullOrWhiteSpace(detected))
        {
            JavaPath = detected;
            return;
        }

        var answer = _services.Dialog.Show(
            $"Javaが見つかりませんでした。\n\nJava {RequiredJavaInstallMajor}（Eclipse Temurin）を自動でセットアップしますか？（約 50 MB・管理者権限不要）\n" +
            "自分で入れる場合は https://adoptium.net から Eclipse Temurin (LTS) をインストールし、もう一度「自動検出」を押すか、「参照」から java.exe を選んでください。",
            "Java 自動検出", MessageBoxButton.YesNo, MessageBoxImage.Question);
        if (answer == MessageBoxResult.Yes)
            await SetupJavaFromSettingsAsync();
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

        if (IsBedrock && !File.Exists(Path.Combine(ServerDirectory, ServerEditions.BedrockExecutableName)))
        {
            issues.Add("bedrock_server.exe が見つかりません。「バージョン」画面の「zip から更新」で統合版サーバーを入れ直してください。");
        }

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
            OnPropertyChanged(nameof(Uptime));
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
        if (IsBedrock)
        {
            Bedrock?.HandleLog(message);
            TrackBedrockPlayers(message);
            return;
        }

        var match = PlayerCountRegex.Match(message);
        if (match.Success && int.TryParse(match.Groups[1].Value, out var count))
            OnlinePlayers = count;
    }

    /// <summary>統合版は参加/退出ログから人数を数える（"Player connected: 名前, xuid: ..."）。</summary>
    private void TrackBedrockPlayers(string message)
    {
        var connected = BedrockPlayerConnectedRegex.Match(message);
        var disconnected = BedrockPlayerDisconnectedRegex.Match(message);
        if (!connected.Success && !disconnected.Success)
            return;

        // 標準出力と標準エラーは別スレッドで届くためロックする
        int count;
        lock (_bedrockOnlinePlayers)
        {
            if (connected.Success)
                _bedrockOnlinePlayers.Add(connected.Groups["name"].Value.Trim());
            if (disconnected.Success)
                _bedrockOnlinePlayers.Remove(disconnected.Groups["name"].Value.Trim());
            count = _bedrockOnlinePlayers.Count;
        }

        WpfApplication.Current?.Dispatcher.BeginInvoke(() => OnlinePlayers = count);
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
        if (Bedrock is not null)
        {
            Bedrock.SettingsSaved -= OnBedrockSettingsSaved;
            Bedrock.Update.Updated -= OnBedrockUpdated;
        }
        _statsTimer.Stop();
        ResourcePack.Dispose();
        GC.SuppressFinalize(this);
    }
}
