using System.Collections.ObjectModel;
using System.Diagnostics;
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
    private readonly AppServices _services;
    private readonly ServerRuntime _runtime;
    private readonly AppSettings _appSettings;
    private readonly DispatcherTimer _statsTimer;
    private readonly ServerConfig _config;
    private ServerSettingsViewModel _settings;
    private string _commandText = string.Empty;
    private bool _restartRequired;
    private string _javaPath = string.Empty;
    private string _publicIp = "-";
    private string _publicIpStatus = string.Empty;
    private string _newWorldName = string.Empty;
    private string _restoreBackupWorldName = string.Empty;
    private bool _createBackupBeforeRestore = true;
    private string _worldBackupStatus = "バックアップ未作成";
    private WorldBackupEntry? _selectedWorldBackup;
    private string _mapArchivePath = string.Empty;
    private string _mapImportWorldName = string.Empty;
    private bool _replaceWorldOnMapImport;
    private string _mapImportStatus = "配布マップ未選択";
    private ArchiveWorldCandidate? _selectedMapArchiveCandidate;
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
    private int _onlinePlayers;
    private TimeSpan _lastCpuTime;
    private DateTime _lastCpuCheck;
    private bool _upnpOpened;
    private AddonEntry? _selectedAddon;
    private string _addonStatus = string.Empty;
    private string _addonImportReview = "未実行";
    private string _addonSearchQuery = string.Empty;
    private string _addonCatalogStatus = "未検索";
    private AddonSearchResult? _selectedAddonSearchResult;
    private bool _isAddonBusy;
    private string _addonProgressMessage = string.Empty;
    private LaunchModeOption? _selectedLaunchMode;
    private StartupPresetOption? _selectedStartupPreset;
    private string _javaExtraArguments = string.Empty;
    private int _memoryXmsMb;
    private int _memoryXmxMb;
    private bool _isDisposed;

    public ServerViewModel(AppServices services, ServerConfig config)
    {
        _services = services;
        _config = config;
        _runtime = _services.Runtime.GetOrCreate(config);
        _runtime.StatusChanged += OnStatusChanged;
        _runtime.LogReceived += OnLogReceived;
        _appSettings = _services.Settings.Load();

        _settings = new ServerSettingsViewModel();
        _settings.PropertyChanged += (_, _) => UpdateRestartRequired();

        Logs = _runtime.Logs;
        Worlds = [];
        WorldBackups = [];
        MapArchiveCandidates = [];
        AvailableVersions = [];
        FilteredAvailableVersions = [];
        VersionFilters = [];
        LanIpAddresses = new ObservableCollection<string>(_services.Network.GetLanIpAddresses());
        ExternalChecklist = new ObservableCollection<string>(
            _services.Network.GetExternalChecklist()
        );
        Ops = [];
        Whitelist = [];
        Addons = [];
        AddonSearchResults = [];
        LaunchModes = [];
        StartupPresets = [];

        AutoRestartOnCrash = _config.AutoRestartOnCrash;
        AutoRestartDelaySeconds = _config.AutoRestartDelaySeconds;
        JavaPath = _config.JavaPath;
        _memoryXmsMb = _config.MemoryXmsMb;
        _memoryXmxMb = _config.MemoryXmxMb;
        _javaExtraArguments = _config.JavaExtraArguments ?? string.Empty;
        InitializeLaunchOptions();
        InitializeVersionFilters();

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
        SwitchWorldCommand = new RelayCommand(
            _ => SwitchWorld(),
            _ => !string.IsNullOrWhiteSpace(SelectedWorld) && !IsSelectedWorldCurrent
        );
        DeleteWorldCommand = new RelayCommand(
            _ => DeleteWorld(),
            _ => !string.IsNullOrWhiteSpace(SelectedWorld)
        );
        CreateWorldCommand = new RelayCommand(
            _ => CreateWorld(),
            _ => !string.IsNullOrWhiteSpace(NewWorldName)
        );
        CreateWorldBackupCommand = new AsyncRelayCommand(
            CreateWorldBackupAsync,
            CanCreateWorldBackup
        );
        RefreshWorldBackupsCommand = new RelayCommand(_ => LoadWorldBackups());
        OpenBackupsDirectoryCommand = new RelayCommand(_ => OpenBackupsDirectory());
        RestoreWorldBackupCommand = new AsyncRelayCommand(
            RestoreWorldBackupAsync,
            CanRestoreWorldBackup
        );
        DeleteWorldBackupCommand = new RelayCommand(DeleteWorldBackup, CanDeleteWorldBackup);
        RefreshWorldsCommand = new RelayCommand(_ => LoadWorlds());
        OpenSelectedWorldDirectoryCommand = new RelayCommand(
            _ => OpenSelectedWorldDirectory(),
            _ => !string.IsNullOrWhiteSpace(SelectedWorld)
        );
        OpenWorldMapCommand = new RelayCommand(
            _ => OpenWorldMap(),
            _ => !string.IsNullOrWhiteSpace(SelectedWorld)
        );
        BrowseMapArchiveCommand = new RelayCommand(_ => BrowseMapArchive());
        ImportMapArchiveCommand = new AsyncRelayCommand(ImportMapArchiveAsync, CanImportMapArchive);
        OpenServerDirectoryCommand = new RelayCommand(_ => OpenServerDirectory());
        OpenLogsDirectoryCommand = new RelayCommand(_ => OpenLogsDirectory());
        OpenCrashReportsCommand = new RelayCommand(_ => OpenCrashReports());
        LoadVersionsCommand = new AsyncRelayCommand(() => LoadVersionsAsync(forceRefresh: true));
        ChangeVersionCommand = new AsyncRelayCommand(
            ChangeVersionAsync,
            () => SelectedVersion is not null
        );
        RedownloadJarCommand = new AsyncRelayCommand(RedownloadJarAsync);
        CreateFirewallRuleCommand = new RelayCommand(_ => CreateFirewallRule());
        DeleteFirewallRuleCommand = new RelayCommand(_ => DeleteFirewallRule());
        RecreateFirewallRuleCommand = new RelayCommand(_ => RecreateFirewallRule());
        OpenPortCommand = new AsyncRelayCommand(OpenPortAsync);
        ClosePortCommand = new AsyncRelayCommand(ClosePortAsync);
        RefreshPublicIpCommand = new AsyncRelayCommand(RefreshPublicIpAsync);
        CopyShareAddressCommand = new RelayCommand(_ => CopyShareAddress());
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
        AddAddonsCommand = new RelayCommand(
            _ => BrowseAndAddAddons(),
            _ => SupportsAddonManagement && !IsAddonBusy
        );
        RefreshAddonsCommand = new RelayCommand(
            _ => LoadAddons(),
            _ => SupportsAddonManagement && !IsAddonBusy
        );
        OpenAddonsDirectoryCommand = new RelayCommand(
            _ => OpenAddonsDirectory(),
            _ => SupportsAddonManagement && !IsAddonBusy
        );
        EnableAddonCommand = new RelayCommand(
            _ => EnableAddon(),
            _ => SelectedAddon is not null && !SelectedAddon.IsEnabled && !IsAddonBusy
        );
        DisableAddonCommand = new RelayCommand(
            _ => DisableAddon(),
            _ => SelectedAddon is not null && SelectedAddon.IsEnabled && !IsAddonBusy
        );
        DeleteAddonCommand = new RelayCommand(
            _ => DeleteAddon(),
            _ => SelectedAddon is not null && !IsAddonBusy
        );
        SearchAddonCatalogCommand = new AsyncRelayCommand(
            SearchAddonCatalogAsync,
            () => SupportsAddonManagement && !string.IsNullOrWhiteSpace(AddonSearchQuery)
        );
        OpenAddonCatalogPageCommand = new RelayCommand(
            _ => OpenSelectedAddonCatalogPage(),
            _ =>
                SelectedAddonSearchResult is not null
                && !string.IsNullOrWhiteSpace(SelectedAddonSearchResult.ProjectUrl)
        );
        ApplyStartupPresetCommand = new RelayCommand(
            _ => ApplyStartupPreset(),
            _ => SelectedStartupPreset is not null
        );
        RunHealthCheckCommand = new RelayCommand(_ => RunHealthCheck(showEvenIfCompleted: true));

        LoadSettings();
        LoadWorlds();
        LoadWorldBackups();
        LoadPermissions();
        LoadAddons();
        _ = LoadVersionsAsync();

        _statsTimer = new DispatcherTimer(
            TimeSpan.FromSeconds(1),
            DispatcherPriority.Background,
            (_, _) => UpdateStats(),
            WpfApplication.Current.Dispatcher
        );
        _statsTimer.Start();

        Status = _runtime.Status;
    }

    public string Name => _config.Name;
    public string ServerId => _config.ServerId;
    public string ServerType => _config.Type;
    public string Version => _config.Version;
    public int Port => _config.Port;
    public int MaxPlayers => _config.MaxPlayers;
    public string ServerDirectory => _config.DirectoryPath;
    public ObservableCollection<string> Logs { get; }
    public ObservableCollection<string> Worlds { get; }
    public ObservableCollection<WorldBackupEntry> WorldBackups { get; }
    public ObservableCollection<ArchiveWorldCandidate> MapArchiveCandidates { get; }
    public ObservableCollection<MinecraftVersionInfo> AvailableVersions { get; }
    public ObservableCollection<MinecraftVersionInfo> FilteredAvailableVersions { get; }
    public ObservableCollection<VersionFilterOption> VersionFilters { get; }
    public ObservableCollection<string> LanIpAddresses { get; }
    public ObservableCollection<string> ExternalChecklist { get; }
    public ObservableCollection<OpEntry> Ops { get; }
    public ObservableCollection<WhitelistEntry> Whitelist { get; }
    public ObservableCollection<AddonEntry> Addons { get; }
    public ObservableCollection<AddonSearchResult> AddonSearchResults { get; }
    public ObservableCollection<LaunchModeOption> LaunchModes { get; }
    public ObservableCollection<StartupPresetOption> StartupPresets { get; }

    public ServerSettingsViewModel Settings
    {
        get => _settings;
        private set => SetProperty(ref _settings, value);
    }

    private ServerStatus _status = ServerStatus.Stopped;
    public ServerStatus Status
    {
        get => _status;
        private set
        {
            if (SetProperty(ref _status, value))
            {
                OnPropertyChanged(nameof(StatusText));
                StartCommand.RaiseCanExecuteChanged();
                StopCommand.RaiseCanExecuteChanged();
                RestartCommand.RaiseCanExecuteChanged();
                SendCommandCommand.RaiseCanExecuteChanged();
                CreateWorldBackupCommand.RaiseCanExecuteChanged();
                RestoreWorldBackupCommand.RaiseCanExecuteChanged();
                ImportMapArchiveCommand.RaiseCanExecuteChanged();
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

    public string LastStartedAtText =>
        _config.LastStartedAt?.ToLocalTime().ToString("yyyy/MM/dd HH:mm") ?? "-";

    public string CommandText
    {
        get => _commandText;
        set => SetProperty(ref _commandText, value);
    }

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
                _services.Configs.Save(_config);
            }
        }
    }

    public string PublicIp
    {
        get => _publicIp;
        private set => SetProperty(ref _publicIp, value);
    }

    public string PublicIpStatus
    {
        get => _publicIpStatus;
        private set => SetProperty(ref _publicIpStatus, value);
    }

    private string? _selectedWorld;
    public string? SelectedWorld
    {
        get => _selectedWorld;
        set
        {
            if (SetProperty(ref _selectedWorld, value))
            {
                SwitchWorldCommand.RaiseCanExecuteChanged();
                DeleteWorldCommand.RaiseCanExecuteChanged();
                OpenSelectedWorldDirectoryCommand.RaiseCanExecuteChanged();
                OpenWorldMapCommand.RaiseCanExecuteChanged();
                CreateWorldBackupCommand.RaiseCanExecuteChanged();
                OnPropertyChanged(nameof(IsSelectedWorldCurrent));
                OnPropertyChanged(nameof(WorldSwitchHint));

                if (string.IsNullOrWhiteSpace(RestoreBackupWorldName) && !string.IsNullOrWhiteSpace(value))
                {
                    RestoreBackupWorldName = value;
                }
            }
        }
    }

    public string CurrentWorldName =>
        string.IsNullOrWhiteSpace(_config.WorldName) ? "-" : _config.WorldName;

    public bool IsSelectedWorldCurrent =>
        !string.IsNullOrWhiteSpace(SelectedWorld)
        && string.Equals(SelectedWorld, _config.WorldName, StringComparison.OrdinalIgnoreCase);

    public string WorldSwitchHint
    {
        get
        {
            if (string.IsNullOrWhiteSpace(SelectedWorld))
            {
                return "切り替え先のワールドを選択してください。";
            }

            if (IsSelectedWorldCurrent)
            {
                return "このワールドが現在適用中です。";
            }

            return "切り替えは server.properties に保存され、次回再起動時に反映されます。";
        }
    }

    public bool SupportsAddonManagement => _services.Addons.Supports(ServerType);

    public string AddonCategoryName => _services.Addons.GetCategoryName(ServerType);

    public string AddonActiveDirectory
    {
        get
        {
            if (!SupportsAddonManagement)
            {
                return "-";
            }

            return _services.Addons.GetActiveDirectoryPath(ServerDirectory, ServerType);
        }
    }

    public string AddonDisabledDirectory
    {
        get
        {
            if (!SupportsAddonManagement)
            {
                return "-";
            }

            return _services.Addons.GetDisabledDirectoryPath(ServerDirectory, ServerType);
        }
    }

    public string AddonStatus
    {
        get => _addonStatus;
        private set => SetProperty(ref _addonStatus, value);
    }

    public string AddonSearchQuery
    {
        get => _addonSearchQuery;
        set
        {
            if (SetProperty(ref _addonSearchQuery, value))
            {
                SearchAddonCatalogCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public string AddonCatalogStatus
    {
        get => _addonCatalogStatus;
        private set => SetProperty(ref _addonCatalogStatus, value);
    }

    public AddonSearchResult? SelectedAddonSearchResult
    {
        get => _selectedAddonSearchResult;
        set
        {
            if (SetProperty(ref _selectedAddonSearchResult, value))
            {
                OpenAddonCatalogPageCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public bool IsAddonBusy
    {
        get => _isAddonBusy;
        private set
        {
            if (SetProperty(ref _isAddonBusy, value))
            {
                RaiseAddonCommandsCanExecuteChanged();
            }
        }
    }

    public string AddonProgressMessage
    {
        get => _addonProgressMessage;
        private set => SetProperty(ref _addonProgressMessage, value);
    }

    public string AddonImportReview
    {
        get => _addonImportReview;
        private set => SetProperty(ref _addonImportReview, value);
    }

    public string AddonGuideText
    {
        get
        {
            if (string.Equals(ServerType, "Fabric", StringComparison.OrdinalIgnoreCase))
            {
                return "Fabric: Fabric対応MOD(.jar)を追加してください。依存MOD（例: fabric-api）が必要な場合があります。";
            }

            if (string.Equals(ServerType, "Forge", StringComparison.OrdinalIgnoreCase))
            {
                return "Forge: Forge対応MOD(.jar)を追加してください。Fabric系は通常動作しません。";
            }

            if (
                string.Equals(ServerType, "Paper", StringComparison.OrdinalIgnoreCase)
                || string.Equals(ServerType, "Purpur", StringComparison.OrdinalIgnoreCase)
                || string.Equals(ServerType, "Spigot", StringComparison.OrdinalIgnoreCase)
            )
            {
                return "Plugin: plugin.yml を含むプラグインを推奨します。MOD系jarは動作しない可能性があります。";
            }

            return "この種別はMOD/プラグイン管理対象外です。";
        }
    }

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
            {
                ApplyStartupPresetCommand?.RaiseCanExecuteChanged();
            }
        }
    }

    public string LaunchModeDescription
    {
        get
        {
            if (!string.Equals(ServerType, "Forge", StringComparison.OrdinalIgnoreCase))
            {
                return "Forge 以外では通常は Auto / server.jar 固定を推奨します。";
            }

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
            {
                next = MemoryXmxMb;
            }

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
            {
                next = MemoryXmsMb;
            }

            if (SetProperty(ref _memoryXmxMb, next))
            {
                _config.MemoryXmxMb = next;
                _services.Configs.Save(_config);
            }
        }
    }

    public AddonEntry? SelectedAddon
    {
        get => _selectedAddon;
        set
        {
            if (SetProperty(ref _selectedAddon, value))
            {
                EnableAddonCommand.RaiseCanExecuteChanged();
                DisableAddonCommand.RaiseCanExecuteChanged();
                DeleteAddonCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public string NewWorldName
    {
        get => _newWorldName;
        set
        {
            if (SetProperty(ref _newWorldName, value))
            {
                CreateWorldCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public string RestoreBackupWorldName
    {
        get => _restoreBackupWorldName;
        set
        {
            if (SetProperty(ref _restoreBackupWorldName, value))
            {
                RestoreWorldBackupCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public bool CreateBackupBeforeRestore
    {
        get => _createBackupBeforeRestore;
        set => SetProperty(ref _createBackupBeforeRestore, value);
    }

    public string WorldBackupStatus
    {
        get => _worldBackupStatus;
        private set => SetProperty(ref _worldBackupStatus, value);
    }

    public WorldBackupEntry? SelectedWorldBackup
    {
        get => _selectedWorldBackup;
        set
        {
            if (SetProperty(ref _selectedWorldBackup, value))
            {
                RestoreWorldBackupCommand.RaiseCanExecuteChanged();
                DeleteWorldBackupCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public string MapArchivePath
    {
        get => _mapArchivePath;
        set
        {
            if (SetProperty(ref _mapArchivePath, value))
            {
                ImportMapArchiveCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public ArchiveWorldCandidate? SelectedMapArchiveCandidate
    {
        get => _selectedMapArchiveCandidate;
        set
        {
            if (SetProperty(ref _selectedMapArchiveCandidate, value))
            {
                OnPropertyChanged(nameof(MapArchiveSourceHint));
                ImportMapArchiveCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public string MapArchiveSourceHint
    {
        get
        {
            if (MapArchiveCandidates.Count == 0)
            {
                return "ZIPを選択すると導入候補を表示します。";
            }

            if (MapArchiveCandidates.Count == 1)
            {
                return "候補は1件です。";
            }

            return $"候補が {MapArchiveCandidates.Count} 件あります。導入元フォルダを選択してください。";
        }
    }

    public string MapImportWorldName
    {
        get => _mapImportWorldName;
        set
        {
            if (SetProperty(ref _mapImportWorldName, value))
            {
                ImportMapArchiveCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public bool ReplaceWorldOnMapImport
    {
        get => _replaceWorldOnMapImport;
        set => SetProperty(ref _replaceWorldOnMapImport, value);
    }

    public string MapImportStatus
    {
        get => _mapImportStatus;
        private set => SetProperty(ref _mapImportStatus, value);
    }

    public VersionFilterOption? SelectedVersionFilter
    {
        get => _selectedVersionFilter;
        set
        {
            if (SetProperty(ref _selectedVersionFilter, value))
            {
                ApplyVersionFilter();
            }
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
            {
                return "バージョン未選択";
            }

            var typeLabel = string.Equals(SelectedVersion.Type, "release", StringComparison.OrdinalIgnoreCase)
                ? "正規版"
                : string.Equals(SelectedVersion.Type, "snapshot", StringComparison.OrdinalIgnoreCase)
                    ? "スナップショット"
                    : SelectedVersion.Type;

            return $"{typeLabel} / {SelectedVersion.ReleaseTime.ToLocalTime():yyyy/MM/dd HH:mm}";
        }
    }

    public OpEntry? SelectedOp
    {
        get => _selectedOp;
        set
        {
            if (SetProperty(ref _selectedOp, value))
            {
                RemoveOpCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public WhitelistEntry? SelectedWhitelist
    {
        get => _selectedWhitelist;
        set
        {
            if (SetProperty(ref _selectedWhitelist, value))
            {
                RemoveWhitelistCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public string NewOpName
    {
        get => _newOpName;
        set
        {
            if (SetProperty(ref _newOpName, value))
            {
                AddOpCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public string NewWhitelistName
    {
        get => _newWhitelistName;
        set
        {
            if (SetProperty(ref _newWhitelistName, value))
            {
                AddWhitelistCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public bool AutoRestartOnCrash
    {
        get => _autoRestartOnCrash;
        set
        {
            if (SetProperty(ref _autoRestartOnCrash, value))
            {
                _config.AutoRestartOnCrash = value;
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
                _services.Configs.Save(_config);
            }
        }
    }

    public double CpuUsagePercent
    {
        get => _cpuUsagePercent;
        private set => SetProperty(ref _cpuUsagePercent, value);
    }

    public double MemoryUsageMb
    {
        get => _memoryUsageMb;
        private set => SetProperty(ref _memoryUsageMb, value);
    }

    public int OnlinePlayers
    {
        get => _onlinePlayers;
        private set
        {
            if (SetProperty(ref _onlinePlayers, value))
            {
                OnPropertyChanged(nameof(PlayerCountText));
            }
        }
    }

    public string PlayerCountText => $"{OnlinePlayers}/{MaxPlayers}";

    public AsyncRelayCommand StartCommand { get; }
    public AsyncRelayCommand StopCommand { get; }
    public AsyncRelayCommand RestartCommand { get; }
    public RelayCommand SendCommandCommand { get; }
    public RelayCommand ExportLogsCommand { get; }
    public RelayCommand SaveSettingsCommand { get; }
    public RelayCommand BrowseJavaCommand { get; }
    public RelayCommand DetectJavaCommand { get; }
    public RelayCommand SwitchWorldCommand { get; }
    public RelayCommand DeleteWorldCommand { get; }
    public RelayCommand CreateWorldCommand { get; }
    public AsyncRelayCommand CreateWorldBackupCommand { get; }
    public RelayCommand RefreshWorldBackupsCommand { get; }
    public RelayCommand OpenBackupsDirectoryCommand { get; }
    public AsyncRelayCommand RestoreWorldBackupCommand { get; }
    public RelayCommand DeleteWorldBackupCommand { get; }
    public RelayCommand RefreshWorldsCommand { get; }
    public RelayCommand OpenSelectedWorldDirectoryCommand { get; }
    public RelayCommand OpenWorldMapCommand { get; }
    public RelayCommand BrowseMapArchiveCommand { get; }
    public AsyncRelayCommand ImportMapArchiveCommand { get; }
    public RelayCommand OpenServerDirectoryCommand { get; }
    public RelayCommand OpenLogsDirectoryCommand { get; }
    public RelayCommand OpenCrashReportsCommand { get; }
    public AsyncRelayCommand LoadVersionsCommand { get; }
    public AsyncRelayCommand ChangeVersionCommand { get; }
    public AsyncRelayCommand RedownloadJarCommand { get; }
    public RelayCommand CreateFirewallRuleCommand { get; }
    public RelayCommand DeleteFirewallRuleCommand { get; }
    public RelayCommand RecreateFirewallRuleCommand { get; }
    public AsyncRelayCommand OpenPortCommand { get; }
    public AsyncRelayCommand ClosePortCommand { get; }
    public AsyncRelayCommand RefreshPublicIpCommand { get; }
    public RelayCommand CopyShareAddressCommand { get; }
    public RelayCommand AddOpCommand { get; }
    public RelayCommand RemoveOpCommand { get; }
    public RelayCommand AddWhitelistCommand { get; }
    public RelayCommand RemoveWhitelistCommand { get; }
    public RelayCommand ReloadPermissionsCommand { get; }
    public RelayCommand AddAddonsCommand { get; }
    public RelayCommand RefreshAddonsCommand { get; }
    public RelayCommand OpenAddonsDirectoryCommand { get; }
    public RelayCommand EnableAddonCommand { get; }
    public RelayCommand DisableAddonCommand { get; }
    public RelayCommand DeleteAddonCommand { get; }
    public AsyncRelayCommand SearchAddonCatalogCommand { get; }
    public RelayCommand OpenAddonCatalogPageCommand { get; }
    public RelayCommand ApplyStartupPresetCommand { get; }
    public RelayCommand RunHealthCheckCommand { get; }

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
        {
            FilteredAvailableVersions.Add(version);
        }

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
        StartupPresets.Add(
            new StartupPresetOption(
                "Balanced",
                "Balanced（推奨）",
                2048,
                4096,
                "-XX:+UseG1GC -XX:+ParallelRefProcEnabled"
            )
        );
        StartupPresets.Add(
            new StartupPresetOption("MemorySaver", "Memory Saver", 1024, 2048, "-XX:+UseG1GC")
        );
        StartupPresets.Add(
            new StartupPresetOption(
                "Throughput",
                "Throughput",
                4096,
                8192,
                "-XX:+UseG1GC -XX:MaxGCPauseMillis=100"
            )
        );

        _selectedLaunchMode =
            LaunchModes.FirstOrDefault(item =>
                string.Equals(
                    item.Id,
                    _config.LaunchModeOverride,
                    StringComparison.OrdinalIgnoreCase
                )
            )
            ?? LaunchModes.FirstOrDefault(item =>
                string.Equals(item.Id, "Auto", StringComparison.OrdinalIgnoreCase)
            );
        OnPropertyChanged(nameof(SelectedLaunchMode));
        OnPropertyChanged(nameof(LaunchModeDescription));

        _selectedStartupPreset =
            StartupPresets.FirstOrDefault(item =>
                string.Equals(item.Id, _config.StartupPresetId, StringComparison.OrdinalIgnoreCase)
            )
            ?? StartupPresets.FirstOrDefault(item =>
                string.Equals(item.Id, "Balanced", StringComparison.OrdinalIgnoreCase)
            );
        OnPropertyChanged(nameof(SelectedStartupPreset));
    }

    private void ApplyStartupPreset()
    {
        if (SelectedStartupPreset is null)
        {
            return;
        }

        MemoryXmsMb = SelectedStartupPreset.MemoryXmsMb;
        MemoryXmxMb = SelectedStartupPreset.MemoryXmxMb;
        JavaExtraArguments = SelectedStartupPreset.JavaExtraArguments;
        _config.StartupPresetId = SelectedStartupPreset.Id;
        _services.Configs.Save(_config);
        _services.Dialog.Show(
            $"起動プリセット「{SelectedStartupPreset.Label}」を適用しました。",
            "完了",
            MessageBoxButton.OK,
            MessageBoxImage.Information
        );
    }

    private async Task StartAsync()
    {
        if (Status != ServerStatus.Stopped)
        {
            return;
        }

        RunHealthCheck(showEvenIfCompleted: false);

        WarnIfJavaVersionMismatch();

        if (!Directory.Exists(ServerDirectory))
        {
            _services.Dialog.Show(
                "サーバーディレクトリが見つかりません。",
                "エラー",
                MessageBoxButton.OK,
                MessageBoxImage.Error
            );
            return;
        }

        if (!EnsurePortAvailable())
        {
            return;
        }

        await TryOpenPortAsync();

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
                "エラー",
                MessageBoxButton.OK,
                MessageBoxImage.Error
            );
        }
    }

    private void RunHealthCheck(bool showEvenIfCompleted)
    {
        if (!showEvenIfCompleted && _config.HasCompletedInitialHealthCheck)
        {
            return;
        }

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
                    "ヘルスチェック",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information
                );
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
        {
            return;
        }

        var javaExe = string.IsNullOrWhiteSpace(JavaPath)
            ? _services.Java.FindJavaExecutable() ?? "java"
            : JavaPath;

        if (
            !_services.Java.TryGetJavaMajorVersion(javaExe, out var actualMajor, out var rawVersion)
        )
        {
            return;
        }

        if (actualMajor >= requiredMajor)
        {
            return;
        }

        var versionText = string.IsNullOrWhiteSpace(rawVersion)
            ? actualMajor.ToString()
            : rawVersion;
        _services.Dialog.Show(
            $"Minecraft {Version} は Java {requiredMajor} 以上が推奨です。現在の Java: {versionText}\n必要に応じて「設定」タブで java.exe を切り替えてください。",
            "Java バージョン警告",
            MessageBoxButton.OK,
            MessageBoxImage.Warning
        );
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
                "エラー",
                MessageBoxButton.OK,
                MessageBoxImage.Error
            );
        }
        finally
        {
            await TryClosePortAsync();
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
                "エラー",
                MessageBoxButton.OK,
                MessageBoxImage.Error
            );
        }
    }

    private void SendCommand()
    {
        var text = CommandText?.Trim();
        if (string.IsNullOrWhiteSpace(text))
        {
            return;
        }

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
                "完了",
                MessageBoxButton.OK,
                MessageBoxImage.Information
            );
        }
        catch (Exception ex)
        {
            _services.Dialog.Show(
                $"ログ出力に失敗しました: {ex.Message}",
                "エラー",
                MessageBoxButton.OK,
                MessageBoxImage.Error
            );
        }
    }

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
        }
        catch (Exception ex)
        {
            _services.Dialog.Show(
                $"設定保存に失敗しました: {ex.Message}",
                "エラー",
                MessageBoxButton.OK,
                MessageBoxImage.Error
            );
        }
    }

    private void BrowseJava()
    {
        var dialog = new OpenFileDialog { Filter = "java.exe|java.exe", FileName = "java.exe" };

        if (dialog.ShowDialog() == true)
        {
            JavaPath = dialog.FileName;
        }
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
                "Java 自動検出",
                MessageBoxButton.OK,
                MessageBoxImage.Information
            );
        }
    }

    private void LoadWorlds()
    {
        Worlds.Clear();
        foreach (var world in _services.Worlds.GetWorlds(ServerDirectory))
        {
            Worlds.Add(world);
        }

        if (string.IsNullOrWhiteSpace(MapImportWorldName))
        {
            MapImportWorldName = _config.WorldName;
        }

        if (string.IsNullOrWhiteSpace(RestoreBackupWorldName))
        {
            RestoreBackupWorldName = _config.WorldName;
        }

        SelectedWorld =
            Worlds.FirstOrDefault(w =>
                string.Equals(w, _config.WorldName, StringComparison.OrdinalIgnoreCase)
            ) ?? Worlds.FirstOrDefault();
        OnPropertyChanged(nameof(CurrentWorldName));
        OnPropertyChanged(nameof(IsSelectedWorldCurrent));
        OnPropertyChanged(nameof(WorldSwitchHint));
        SwitchWorldCommand.RaiseCanExecuteChanged();
        OpenSelectedWorldDirectoryCommand.RaiseCanExecuteChanged();
        OpenWorldMapCommand.RaiseCanExecuteChanged();
        CreateWorldBackupCommand.RaiseCanExecuteChanged();
        RestoreWorldBackupCommand.RaiseCanExecuteChanged();
        ImportMapArchiveCommand.RaiseCanExecuteChanged();
    }

    private void OpenSelectedWorldDirectory()
    {
        if (string.IsNullOrWhiteSpace(SelectedWorld))
        {
            return;
        }

        var path = Path.Combine(ServerDirectory, SelectedWorld);
        OpenDirectory(path, "選択したワールドフォルダが見つかりません。");
    }

    private void OpenWorldMap()
    {
        if (string.IsNullOrWhiteSpace(SelectedWorld))
            return;

        var worldPath = Path.Combine(ServerDirectory, SelectedWorld);
        var vm = new WorldMapViewModel(_services.WorldMap, worldPath);
        var window = new Views.WorldMapWindow(vm)
        {
            Owner = WpfApplication.Current?.MainWindow
        };
        window.Show();
    }

    private void OpenServerDirectory()
    {
        OpenDirectory(ServerDirectory, "サーバーディレクトリが見つかりません。");
    }

    private void OpenLogsDirectory()
    {
        var path = Path.Combine(ServerDirectory, "logs");
        OpenDirectory(path, "ログフォルダが見つかりません。");
    }

    private void OpenCrashReports()
    {
        var path = Path.Combine(ServerDirectory, "crash-reports");
        OpenDirectory(path, "クラッシュレポートが見つかりません。");
    }

    private void OpenDirectory(string path, string missingMessage)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(path) || !Directory.Exists(path))
            {
                _services.Dialog.Show(
                    missingMessage,
                    "エラー",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error
                );
                return;
            }

            Process.Start(new ProcessStartInfo { FileName = path, UseShellExecute = true });
        }
        catch (Exception ex)
        {
            _services.Dialog.Show(
                $"フォルダを開けませんでした: {ex.Message}",
                "エラー",
                MessageBoxButton.OK,
                MessageBoxImage.Error
            );
        }
    }

    private void OpenUrl(string url)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(url))
            {
                return;
            }

            Process.Start(new ProcessStartInfo { FileName = url, UseShellExecute = true });
        }
        catch (Exception ex)
        {
            _services.Dialog.Show(
                $"ブラウザを開けませんでした: {ex.Message}",
                "エラー",
                MessageBoxButton.OK,
                MessageBoxImage.Error
            );
        }
    }

    private void CreateWorld()
    {
        try
        {
            _services.Worlds.CreateWorldFolder(ServerDirectory, NewWorldName.Trim());
            NewWorldName = string.Empty;
            LoadWorlds();
        }
        catch (Exception ex)
        {
            _services.Dialog.Show(
                $"ワールド作成に失敗しました: {ex.Message}",
                "エラー",
                MessageBoxButton.OK,
                MessageBoxImage.Error
            );
        }
    }

    private void SwitchWorld()
    {
        if (string.IsNullOrWhiteSpace(SelectedWorld))
        {
            return;
        }

        var props = Settings.ToModel();
        props.LevelName = SelectedWorld;
        _services.Properties.Save(ServerDirectory, props);
        _config.WorldName = SelectedWorld;
        _services.Configs.Save(_config);
        Settings.Load(props);
        UpdateRestartRequired();
        OnPropertyChanged(nameof(CurrentWorldName));
        OnPropertyChanged(nameof(IsSelectedWorldCurrent));
        OnPropertyChanged(nameof(WorldSwitchHint));
        SwitchWorldCommand.RaiseCanExecuteChanged();
    }

    private void DeleteWorld()
    {
        if (string.IsNullOrWhiteSpace(SelectedWorld))
        {
            return;
        }

        if (Status != ServerStatus.Stopped)
        {
            _services.Dialog.Show(
                "停止中のみ削除できます。",
                "確認",
                MessageBoxButton.OK,
                MessageBoxImage.Information
            );
            return;
        }

        if (
            _services.Dialog.Show(
                $"ワールド {SelectedWorld} を削除します。",
                "確認",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning
            ) != MessageBoxResult.Yes
        )
        {
            return;
        }

        try
        {
            _services.Worlds.DeleteWorld(ServerDirectory, SelectedWorld);
            if (string.Equals(_config.WorldName, SelectedWorld, StringComparison.OrdinalIgnoreCase))
            {
                _config.WorldName = "world";
                _services.Configs.Save(_config);
                OnPropertyChanged(nameof(CurrentWorldName));
            }
            LoadWorlds();
        }
        catch (Exception ex)
        {
            _services.Dialog.Show(
                $"削除に失敗しました: {ex.Message}",
                "エラー",
                MessageBoxButton.OK,
                MessageBoxImage.Error
            );
        }
    }

    private void LoadWorldBackups()
    {
        var previousSelectedPath = SelectedWorldBackup?.FullPath;
        WorldBackups.Clear();
        foreach (var backup in _services.Worlds.GetWorldBackups(ServerDirectory))
        {
            WorldBackups.Add(backup);
        }

        if (!string.IsNullOrWhiteSpace(previousSelectedPath))
        {
            SelectedWorldBackup = WorldBackups.FirstOrDefault(entry =>
                string.Equals(entry.FullPath, previousSelectedPath, StringComparison.OrdinalIgnoreCase)
            );
        }

        SelectedWorldBackup ??= WorldBackups.FirstOrDefault();
        if (WorldBackups.Count == 0)
        {
            WorldBackupStatus = "バックアップはまだありません。";
        }
        else if (string.IsNullOrWhiteSpace(WorldBackupStatus) || WorldBackupStatus == "バックアップ未作成")
        {
            WorldBackupStatus = $"バックアップ {WorldBackups.Count} 件";
        }

        RestoreWorldBackupCommand.RaiseCanExecuteChanged();
        DeleteWorldBackupCommand.RaiseCanExecuteChanged();
    }

    private bool CanCreateWorldBackup()
    {
        return Status == ServerStatus.Stopped && !string.IsNullOrWhiteSpace(SelectedWorld);
    }

    private async Task CreateWorldBackupAsync()
    {
        if (Status != ServerStatus.Stopped)
        {
            _services.Dialog.Show(
                "停止中のみバックアップできます。",
                "確認",
                MessageBoxButton.OK,
                MessageBoxImage.Information
            );
            return;
        }

        var worldName = SelectedWorld?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(worldName))
        {
            _services.Dialog.Show(
                "バックアップ対象のワールドを選択してください。",
                "確認",
                MessageBoxButton.OK,
                MessageBoxImage.Information
            );
            return;
        }

        try
        {
            WorldBackupStatus = "バックアップを作成しています...";
            var created = await Task.Run(() =>
                _services.Worlds.CreateWorldBackup(ServerDirectory, worldName)
            );

            LoadWorldBackups();
            SelectedWorldBackup = WorldBackups.FirstOrDefault(entry =>
                string.Equals(entry.FullPath, created.FullPath, StringComparison.OrdinalIgnoreCase)
            );
            WorldBackupStatus = $"バックアップ作成完了: {created.FileName}";
        }
        catch (Exception ex)
        {
            WorldBackupStatus = "バックアップ作成に失敗しました。";
            _services.Dialog.Show(
                $"バックアップ作成に失敗しました: {ex.Message}",
                "エラー",
                MessageBoxButton.OK,
                MessageBoxImage.Error
            );
        }
    }

    private bool CanRestoreWorldBackup()
    {
        return Status == ServerStatus.Stopped
            && SelectedWorldBackup is not null
            && !string.IsNullOrWhiteSpace(RestoreBackupWorldName);
    }

    private async Task RestoreWorldBackupAsync()
    {
        if (Status != ServerStatus.Stopped)
        {
            _services.Dialog.Show(
                "停止中のみ復元できます。",
                "確認",
                MessageBoxButton.OK,
                MessageBoxImage.Information
            );
            return;
        }

        var backup = SelectedWorldBackup;
        var targetWorldName = RestoreBackupWorldName?.Trim() ?? string.Empty;
        if (backup is null || string.IsNullOrWhiteSpace(targetWorldName))
        {
            WorldBackupStatus = "復元先ワールド名とバックアップを指定してください。";
            return;
        }

        var targetDirectory = Path.Combine(ServerDirectory, targetWorldName);
        if (Directory.Exists(targetDirectory))
        {
            var result = _services.Dialog.Show(
                $"ワールド {targetWorldName} をバックアップ {backup.FileName} で上書き復元します。続行しますか？",
                "確認",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning
            );
            if (result != MessageBoxResult.Yes)
            {
                WorldBackupStatus = "復元をキャンセルしました。";
                return;
            }
        }

        try
        {
            WorldBackupStatus = "バックアップを復元しています...";
            var sourceWorld = await Task.Run(() =>
                _services.Worlds.RestoreWorldBackup(
                    ServerDirectory,
                    backup.FullPath,
                    targetWorldName,
                    overwriteExisting: true,
                    createBackupBeforeRestore: CreateBackupBeforeRestore
                )
            );

            LoadWorlds();
            LoadWorldBackups();
            SelectedWorld = targetWorldName;
            SwitchWorld();
            WorldBackupStatus = $"復元完了: {backup.FileName} -> {targetWorldName}";
            _services.Dialog.Show(
                $"バックアップ ({backup.FileName}) を {targetWorldName} に復元しました。導入元: {sourceWorld}",
                "完了",
                MessageBoxButton.OK,
                MessageBoxImage.Information
            );
        }
        catch (Exception ex)
        {
            WorldBackupStatus = "復元に失敗しました。";
            _services.Dialog.Show(
                $"バックアップ復元に失敗しました: {ex.Message}",
                "エラー",
                MessageBoxButton.OK,
                MessageBoxImage.Error
            );
        }
    }

    private bool CanDeleteWorldBackup()
    {
        return SelectedWorldBackup is not null;
    }

    private void DeleteWorldBackup()
    {
        var backup = SelectedWorldBackup;
        if (backup is null)
        {
            return;
        }

        if (
            _services.Dialog.Show(
                $"バックアップ {backup.FileName} を削除します。",
                "確認",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning
            ) != MessageBoxResult.Yes
        )
        {
            return;
        }

        try
        {
            _services.Worlds.DeleteWorldBackup(backup.FullPath);
            LoadWorldBackups();
            WorldBackupStatus = $"バックアップを削除しました: {backup.FileName}";
        }
        catch (Exception ex)
        {
            _services.Dialog.Show(
                $"バックアップ削除に失敗しました: {ex.Message}",
                "エラー",
                MessageBoxButton.OK,
                MessageBoxImage.Error
            );
        }
    }

    private void OpenBackupsDirectory()
    {
        var backupsDirectory = _services.Worlds.GetBackupsDirectory(ServerDirectory);
        Directory.CreateDirectory(backupsDirectory);
        OpenDirectory(backupsDirectory, "バックアップフォルダが見つかりません。");
    }

    private bool CanImportMapArchive()
    {
        return Status == ServerStatus.Stopped
            && !string.IsNullOrWhiteSpace(MapArchivePath)
            && File.Exists(MapArchivePath)
            && !string.IsNullOrWhiteSpace(MapImportWorldName)
            && SelectedMapArchiveCandidate is not null;
    }

    private void BrowseMapArchive()
    {
        var dialog = new OpenFileDialog
        {
            Filter = "ZIP ファイル|*.zip",
            CheckFileExists = true,
            Multiselect = false,
        };

        if (dialog.ShowDialog() != true)
        {
            return;
        }

        MapArchivePath = dialog.FileName;
        if (!LoadMapArchiveCandidates(dialog.FileName))
        {
            return;
        }
    }

    private async Task ImportMapArchiveAsync()
    {
        if (Status != ServerStatus.Stopped)
        {
            _services.Dialog.Show(
                "停止中のみ配布マップを導入できます。",
                "確認",
                MessageBoxButton.OK,
                MessageBoxImage.Information
            );
            return;
        }

        var archivePath = MapArchivePath?.Trim() ?? string.Empty;
        var worldName = MapImportWorldName?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(archivePath) || string.IsNullOrWhiteSpace(worldName))
        {
            MapImportStatus = "ZIP と導入先ワールド名を指定してください。";
            return;
        }

        if (!File.Exists(archivePath))
        {
            MapImportStatus = "ZIP ファイルが見つかりません。";
            return;
        }

        var selectedCandidate = SelectedMapArchiveCandidate;
        if (selectedCandidate is null)
        {
            MapImportStatus = "導入元フォルダを選択してください。";
            return;
        }

        if (!Settings.EnableCommandBlock)
        {
            var result = _services.Dialog.Show(
                "配布マップにはコマンドブロックが必要な場合があります。\n有効にしますか？（有効にしない場合、正常に動作しない恐れがあります）",
                "コマンドブロック設定",
                MessageBoxButton.YesNoCancel,
                MessageBoxImage.Warning
            );

            if (result == MessageBoxResult.Cancel)
            {
                MapImportStatus = "導入をキャンセルしました。";
                return;
            }

            if (result == MessageBoxResult.Yes)
            {
                Settings.EnableCommandBlock = true;
                SaveSettings();
            }
        }

        var targetDirectory = Path.Combine(ServerDirectory, worldName);
        if (Directory.Exists(targetDirectory) && !ReplaceWorldOnMapImport)
        {
            MapImportStatus = "同名ワールドが存在します。上書き設定を有効にしてください。";
            _services.Dialog.Show(
                $"ワールド {worldName} は既に存在します。上書きして導入する場合はチェックを有効にしてください。",
                "確認",
                MessageBoxButton.OK,
                MessageBoxImage.Information
            );
            return;
        }

        if (Directory.Exists(targetDirectory) && ReplaceWorldOnMapImport)
        {
            var confirm = _services.Dialog.Show(
                $"ワールド {worldName} は既に存在します。上書きして導入しますか？",
                "確認",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning
            );
            if (confirm != MessageBoxResult.Yes)
            {
                MapImportStatus = "導入をキャンセルしました。";
                return;
            }
        }

        try
        {
            MapImportStatus = "配布マップを導入しています...";
            var importedSource = await Task.Run(() =>
                _services.Worlds.ImportWorldArchive(
                    ServerDirectory,
                    archivePath,
                    worldName,
                    ReplaceWorldOnMapImport,
                    selectedCandidate.RelativePath
                )
            );

            LoadWorlds();
            SelectedWorld = worldName;
            SwitchWorld();

            MapImportStatus = $"導入完了: {worldName}";
            _services.Dialog.Show(
                $"配布マップ ({importedSource}) を {worldName} として導入しました。次回起動時にこのワールドが適用されます。",
                "完了",
                MessageBoxButton.OK,
                MessageBoxImage.Information
            );
        }
        catch (Exception ex)
        {
            MapImportStatus = "導入に失敗しました。";
            _services.Dialog.Show(
                $"配布マップ導入に失敗しました: {ex.Message}",
                "エラー",
                MessageBoxButton.OK,
                MessageBoxImage.Error
            );
        }
    }

    private bool LoadMapArchiveCandidates(string archivePath)
    {
        try
        {
            MapArchiveCandidates.Clear();
            SelectedMapArchiveCandidate = null;

            foreach (var candidate in _services.Worlds.GetArchiveWorldCandidates(archivePath))
            {
                MapArchiveCandidates.Add(candidate);
            }

            SelectedMapArchiveCandidate = MapArchiveCandidates.FirstOrDefault();
            OnPropertyChanged(nameof(MapArchiveSourceHint));

            if (MapArchiveCandidates.Count == 0)
            {
                MapImportStatus = "ZIP内にワールドデータが見つかりません。";
                _services.Dialog.Show(
                    "ZIP内に導入可能なワールドが見つかりませんでした。level.dat または region を含むワールドを選択してください。",
                    "確認",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning
                );
                return false;
            }

            if (string.IsNullOrWhiteSpace(MapImportWorldName))
            {
                MapImportWorldName = SuggestWorldNameFromCandidate(
                    SelectedMapArchiveCandidate,
                    archivePath
                );
            }

            MapImportStatus =
                MapArchiveCandidates.Count == 1
                    ? "導入元フォルダを自動選択しました。"
                    : $"導入元候補が {MapArchiveCandidates.Count} 件見つかりました。正しいフォルダを選択してください。";
            return true;
        }
        catch (Exception ex)
        {
            MapArchiveCandidates.Clear();
            SelectedMapArchiveCandidate = null;
            OnPropertyChanged(nameof(MapArchiveSourceHint));
            MapImportStatus = "ZIPの解析に失敗しました。";
            _services.Dialog.Show(
                $"配布マップZIPの解析に失敗しました: {ex.Message}",
                "エラー",
                MessageBoxButton.OK,
                MessageBoxImage.Error
            );
            return false;
        }
    }

    private static string SuggestWorldNameFromCandidate(
        ArchiveWorldCandidate? candidate,
        string archivePath
    )
    {
        if (candidate is not null && !string.IsNullOrWhiteSpace(candidate.RelativePath))
        {
            var name = candidate
                .RelativePath.Split(['/', '\\'], StringSplitOptions.RemoveEmptyEntries)
                .LastOrDefault();
            var sanitized = SanitizeWorldName(name ?? string.Empty);
            if (!string.IsNullOrWhiteSpace(sanitized))
            {
                return sanitized;
            }
        }

        return SuggestWorldNameFromArchive(archivePath);
    }

    private static string SuggestWorldNameFromArchive(string archivePath)
    {
        var raw = Path.GetFileNameWithoutExtension(archivePath);
        return SanitizeWorldName(raw);
    }

    private static string SanitizeWorldName(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return "world";
        }

        var cleaned = new string([
            .. raw.Where(ch => !Path.GetInvalidFileNameChars().Contains(ch)),
        ]).Trim();
        return string.IsNullOrWhiteSpace(cleaned) ? "world" : cleaned;
    }

    private async Task LoadVersionsAsync(bool forceRefresh = false)
    {
        try
        {
            var versions = await _services.Versions.GetVersionsAsync(forceRefresh);
            WpfApplication.Current.Dispatcher.Invoke(() =>
            {
                AvailableVersions.Clear();
                foreach (var version in versions)
                {
                    AvailableVersions.Add(version);
                }
                ApplyVersionFilter();
            });
        }
        catch (Exception ex)
        {
            _services.Dialog.Show(
                $"バージョン取得に失敗しました: {ex.Message}",
                "エラー",
                MessageBoxButton.OK,
                MessageBoxImage.Error
            );
        }
    }

    private async Task ChangeVersionAsync()
    {
        if (SelectedVersion is null)
        {
            return;
        }

        if (Status != ServerStatus.Stopped)
        {
            _services.Dialog.Show(
                "停止中のみ変更できます。",
                "確認",
                MessageBoxButton.OK,
                MessageBoxImage.Information
            );
            return;
        }

        if (
            IsModdedServerType(ServerType)
            && !string.Equals(SelectedVersion.Type, "release", StringComparison.OrdinalIgnoreCase)
        )
        {
            var modWarningResult = _services.Dialog.Show(
                $"選択中の {SelectedVersion.Id} は {SelectedVersion.Type} です。Forge/Fabric では動作しない可能性があります。続行しますか？",
                "バージョン確認",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning
            );
            if (modWarningResult != MessageBoxResult.Yes)
            {
                return;
            }
        }

        if (
            _services.Dialog.Show(
                $"バージョンを {SelectedVersion.Id} に変更します。",
                "確認",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question
            ) != MessageBoxResult.Yes
        )
        {
            return;
        }

        try
        {
            var jarPath = Path.Combine(ServerDirectory, "server.jar");
            await _services.Jars.DownloadAsync(
                _config.Type,
                SelectedVersion.Id,
                jarPath,
                _config.JavaPath
            );
            _config.Version = SelectedVersion.Id;
            _services.Configs.Save(_config);
            OnPropertyChanged(nameof(Version));
        }
        catch (Exception ex)
        {
            _services.Dialog.Show(
                $"バージョン変更に失敗しました: {ex.Message}",
                "エラー",
                MessageBoxButton.OK,
                MessageBoxImage.Error
            );
        }
    }

    private async Task RedownloadJarAsync()
    {
        if (Status != ServerStatus.Stopped)
        {
            _services.Dialog.Show(
                "停止中のみ再ダウンロードできます。",
                "確認",
                MessageBoxButton.OK,
                MessageBoxImage.Information
            );
            return;
        }

        try
        {
            var jarPath = Path.Combine(ServerDirectory, "server.jar");
            await _services.Jars.DownloadAsync(
                _config.Type,
                _config.Version,
                jarPath,
                _config.JavaPath
            );
        }
        catch (Exception ex)
        {
            _services.Dialog.Show(
                $"再ダウンロードに失敗しました: {ex.Message}",
                "エラー",
                MessageBoxButton.OK,
                MessageBoxImage.Error
            );
        }
    }

    private void CreateFirewallRule()
    {
        try
        {
            if (string.IsNullOrWhiteSpace(_config.Firewall.TcpRuleName))
            {
                _config.Firewall = _services.Firewall.BuildRuleInfo(_config.ServerId);
            }

            _services.Firewall.CreateRules(_config.Port, _config.Firewall);
            _services.Configs.Save(_config);
            _services.Dialog.Show(
                "Firewall ルールを作成しました。",
                "完了",
                MessageBoxButton.OK,
                MessageBoxImage.Information
            );
        }
        catch (Exception ex)
        {
            _services.Dialog.Show(
                $"Firewall ルール作成に失敗しました: {ex.Message}",
                "エラー",
                MessageBoxButton.OK,
                MessageBoxImage.Error
            );
        }
    }

    private void DeleteFirewallRule()
    {
        try
        {
            _services.Firewall.DeleteRules(_config.Firewall);
            _services.Dialog.Show(
                "Firewall ルールを削除しました。",
                "完了",
                MessageBoxButton.OK,
                MessageBoxImage.Information
            );
        }
        catch (Exception ex)
        {
            _services.Dialog.Show(
                $"Firewall ルール削除に失敗しました: {ex.Message}",
                "エラー",
                MessageBoxButton.OK,
                MessageBoxImage.Error
            );
        }
    }

    private void RecreateFirewallRule()
    {
        try
        {
            if (string.IsNullOrWhiteSpace(_config.Firewall.TcpRuleName))
            {
                _config.Firewall = _services.Firewall.BuildRuleInfo(_config.ServerId);
            }

            _services.Firewall.RecreateRules(_config.Port, _config.Firewall);
            _services.Configs.Save(_config);
            _services.Dialog.Show(
                "Firewall ルールを再作成しました。",
                "完了",
                MessageBoxButton.OK,
                MessageBoxImage.Information
            );
        }
        catch (Exception ex)
        {
            _services.Dialog.Show(
                $"Firewall ルール再作成に失敗しました: {ex.Message}",
                "エラー",
                MessageBoxButton.OK,
                MessageBoxImage.Error
            );
        }
    }

    private async Task OpenPortAsync()
    {
        var (ok, error) = await _services.Upnp.TryOpenPortAsync(
            _config.Port,
            $"McServerManager_{_config.Name}"
        );
        if (!ok)
        {
            _services.Dialog.Show(
                error ?? "ポート開放に失敗しました。",
                "警告",
                MessageBoxButton.OK,
                MessageBoxImage.Warning
            );
            return;
        }

        _upnpOpened = true;
        _services.Dialog.Show(
            $"TCP {_config.Port} のポート開放を実行しました。",
            "完了",
            MessageBoxButton.OK,
            MessageBoxImage.Information
        );
    }

    private async Task ClosePortAsync()
    {
        await _services.Upnp.TryClosePortAsync(_config.Port);
        _upnpOpened = false;
        _services.Dialog.Show(
            $"TCP {_config.Port} のポート閉鎖を実行しました。",
            "完了",
            MessageBoxButton.OK,
            MessageBoxImage.Information
        );
    }

    private async Task RefreshPublicIpAsync()
    {
        PublicIpStatus = "取得中...";
        var ip = await _services.Network.GetPublicIpAsync();
        PublicIp = string.IsNullOrWhiteSpace(ip) ? "-" : ip;
        PublicIpStatus = string.IsNullOrWhiteSpace(ip) ? "取得失敗" : string.Empty;
    }

    private void CopyShareAddress()
    {
        var address = PublicIp;
        try
        {
            System.Windows.Clipboard.SetText(address);
        }
        catch
        {
            // Clipboard may fail in rare cases
        }
    }

    private void LoadPermissions()
    {
        Ops.Clear();
        foreach (var op in _services.Permissions.LoadOps(ServerDirectory))
        {
            Ops.Add(op);
        }

        Whitelist.Clear();
        foreach (var entry in _services.Permissions.LoadWhitelist(ServerDirectory))
        {
            Whitelist.Add(entry);
        }
    }

    private void LoadAddons()
    {
        Addons.Clear();
        SelectedAddon = null;

        if (!SupportsAddonManagement)
        {
            AddonStatus = "このサーバー種別はMOD/プラグイン管理の対象外です。";
            AddonImportReview = "対象外";
            AddonCatalogStatus = "対象外";
            AddonSearchResults.Clear();
            SelectedAddonSearchResult = null;
            RaiseAddonCommandsCanExecuteChanged();
            OnPropertyChanged(nameof(SupportsAddonManagement));
            OnPropertyChanged(nameof(AddonCategoryName));
            OnPropertyChanged(nameof(AddonActiveDirectory));
            OnPropertyChanged(nameof(AddonDisabledDirectory));
            OnPropertyChanged(nameof(AddonGuideText));
            return;
        }

        try
        {
            foreach (var addon in _services.Addons.GetAddons(ServerDirectory, ServerType))
            {
                Addons.Add(addon);
            }

            AddonStatus =
                Addons.Count == 0
                    ? $"{AddonCategoryName}ファイルがまだありません。"
                    : $"{AddonCategoryName} {Addons.Count} 件";

            var warnings = _services.Addons.AnalyzeCompatibilityWarnings(ServerType, Addons);
            if (warnings.Count > 0)
            {
                AddonStatus += $" / 警告 {warnings.Count} 件";
            }

            if (string.Equals(AddonImportReview, "未実行", StringComparison.OrdinalIgnoreCase))
            {
                AddonImportReview = "追加前チェックを実行してください。";
            }
        }
        catch (Exception ex)
        {
            AddonStatus = "一覧取得に失敗しました。";
            _services.Dialog.Show(
                $"一覧取得に失敗しました: {ex.Message}",
                "エラー",
                MessageBoxButton.OK,
                MessageBoxImage.Error
            );
        }

        RaiseAddonCommandsCanExecuteChanged();
        OnPropertyChanged(nameof(SupportsAddonManagement));
        OnPropertyChanged(nameof(AddonCategoryName));
        OnPropertyChanged(nameof(AddonActiveDirectory));
        OnPropertyChanged(nameof(AddonDisabledDirectory));
        OnPropertyChanged(nameof(AddonGuideText));
    }

    private void BrowseAndAddAddons()
    {
        if (!SupportsAddonManagement)
        {
            return;
        }

        var dialog = new OpenFileDialog
        {
            Filter = "jarファイル|*.jar",
            Multiselect = true,
            CheckFileExists = true,
        };

        if (dialog.ShowDialog() == true)
        {
            _ = ImportAddonsAsync(dialog.FileNames);
        }
    }

    public async Task ImportAddonsAsync(IEnumerable<string> sourcePaths)
    {
        if (!SupportsAddonManagement || IsAddonBusy)
        {
            return;
        }

        try
        {
            IsAddonBusy = true;
            AddonProgressMessage = "追加前チェックを実行しています...";
            await Task.Yield();

            var assessment = _services.Addons.AssessImport(ServerType, sourcePaths, Addons);
            AddonImportReview = assessment.Summary;

            if (assessment.CandidateCount == 0)
            {
                AddonStatus = "追加できるjarファイルが見つかりませんでした。";
                return;
            }

            if (assessment.RequiresConfirmation)
            {
                var detailLines = assessment
                    .Warnings.Take(12)
                    .Select((warning, index) => $"{index + 1}. {warning}")
                    .ToList();

                if (assessment.Warnings.Count > detailLines.Count)
                {
                    detailLines.Add($"...他 {assessment.Warnings.Count - detailLines.Count} 件");
                }

                var details = string.Join(Environment.NewLine, detailLines);
                var result = _services.Dialog.Show(
                    $"追加前チェックで注意点が見つかりました。続行しますか？{Environment.NewLine}{Environment.NewLine}{details}",
                    "追加前チェック",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Warning
                );

                if (result != MessageBoxResult.Yes)
                {
                    AddonStatus = "追加をキャンセルしました。";
                    return;
                }
            }

            AddonProgressMessage = "ファイルを追加しています...";
            var count = await Task.Run(() =>
                _services.Addons.AddFiles(ServerDirectory, ServerType, sourcePaths)
            );
            if (count > 0)
            {
                AddonStatus = $"{count} 件を追加しました。";
            }
            else
            {
                AddonStatus = "追加できるjarファイルが見つかりませんでした。";
            }

            AddonProgressMessage = "一覧を更新しています...";
            LoadAddons();
            ShowAddonCompatibilityWarnings();
        }
        catch (Exception ex)
        {
            _services.Dialog.Show(
                $"追加に失敗しました: {ex.Message}",
                "エラー",
                MessageBoxButton.OK,
                MessageBoxImage.Error
            );
        }
        finally
        {
            AddonProgressMessage = string.Empty;
            IsAddonBusy = false;
        }
    }

    private void ShowAddonCompatibilityWarnings()
    {
        var warnings = _services.Addons.AnalyzeCompatibilityWarnings(ServerType, Addons);
        if (warnings.Count == 0)
        {
            return;
        }

        var lines = string.Join(
            Environment.NewLine,
            warnings.Select((warning, index) => $"{index + 1}. {warning}")
        );
        _services.Dialog.Show(
            $"追加した {AddonCategoryName} に互換性の注意点があります。{Environment.NewLine}{Environment.NewLine}{lines}",
            "互換性チェック",
            MessageBoxButton.OK,
            MessageBoxImage.Warning
        );
    }

    private void DisableAddon()
    {
        if (SelectedAddon is null)
        {
            return;
        }

        try
        {
            _services.Addons.Disable(ServerDirectory, ServerType, SelectedAddon);
            LoadAddons();
        }
        catch (Exception ex)
        {
            _services.Dialog.Show(
                $"無効化に失敗しました: {ex.Message}",
                "エラー",
                MessageBoxButton.OK,
                MessageBoxImage.Error
            );
        }
    }

    private void EnableAddon()
    {
        if (SelectedAddon is null)
        {
            return;
        }

        try
        {
            _services.Addons.Enable(ServerDirectory, ServerType, SelectedAddon);
            LoadAddons();
        }
        catch (Exception ex)
        {
            _services.Dialog.Show(
                $"有効化に失敗しました: {ex.Message}",
                "エラー",
                MessageBoxButton.OK,
                MessageBoxImage.Error
            );
        }
    }

    private void DeleteAddon()
    {
        if (SelectedAddon is null)
        {
            return;
        }

        if (
            _services.Dialog.Show(
                $"{SelectedAddon.FileName} を削除します。",
                "確認",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning
            ) != MessageBoxResult.Yes
        )
        {
            return;
        }

        try
        {
            _services.Addons.Delete(SelectedAddon);
            LoadAddons();
        }
        catch (Exception ex)
        {
            _services.Dialog.Show(
                $"削除に失敗しました: {ex.Message}",
                "エラー",
                MessageBoxButton.OK,
                MessageBoxImage.Error
            );
        }
    }

    private void OpenAddonsDirectory()
    {
        if (!SupportsAddonManagement)
        {
            return;
        }

        OpenDirectory(AddonActiveDirectory, $"{AddonCategoryName}フォルダが見つかりません。");
    }

    private async Task SearchAddonCatalogAsync()
    {
        if (!SupportsAddonManagement)
        {
            return;
        }

        var query = AddonSearchQuery?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(query))
        {
            AddonCatalogStatus = "検索キーワードを入力してください。";
            return;
        }

        try
        {
            AddonCatalogStatus = "検索中...";
            var results = await _services
                .AddonCatalog.SearchAsync(query, ServerType, Version, limit: 20)
                .ConfigureAwait(false);

            WpfApplication.Current.Dispatcher.Invoke(() =>
            {
                AddonSearchResults.Clear();
                foreach (var result in results)
                {
                    AddonSearchResults.Add(result);
                }

                SelectedAddonSearchResult = AddonSearchResults.FirstOrDefault();
            });

            AddonCatalogStatus =
                results.Count == 0
                    ? "該当する候補が見つかりませんでした。"
                    : $"{results.Count} 件見つかりました。";
        }
        catch (Exception ex)
        {
            AddonCatalogStatus = "検索に失敗しました。";
            _services.Dialog.Show(
                $"Modrinth検索に失敗しました: {ex.Message}",
                "エラー",
                MessageBoxButton.OK,
                MessageBoxImage.Error
            );
        }
    }

    private void OpenSelectedAddonCatalogPage()
    {
        if (SelectedAddonSearchResult is null)
        {
            return;
        }

        OpenUrl(SelectedAddonSearchResult.ProjectUrl);
    }

    private void RaiseAddonCommandsCanExecuteChanged()
    {
        AddAddonsCommand.RaiseCanExecuteChanged();
        RefreshAddonsCommand.RaiseCanExecuteChanged();
        OpenAddonsDirectoryCommand.RaiseCanExecuteChanged();
        EnableAddonCommand.RaiseCanExecuteChanged();
        DisableAddonCommand.RaiseCanExecuteChanged();
        DeleteAddonCommand.RaiseCanExecuteChanged();
        SearchAddonCatalogCommand.RaiseCanExecuteChanged();
        OpenAddonCatalogPageCommand.RaiseCanExecuteChanged();
    }

    private void AddOp()
    {
        var name = NewOpName.Trim();
        if (string.IsNullOrWhiteSpace(name))
        {
            return;
        }

        if (Ops.Any(o => string.Equals(o.Name, name, StringComparison.OrdinalIgnoreCase)))
        {
            return;
        }

        Ops.Add(new OpEntry { Name = name });
        _services.Permissions.SaveOps(ServerDirectory, Ops);
        NewOpName = string.Empty;
    }

    private void RemoveOp()
    {
        if (SelectedOp is null)
        {
            return;
        }

        Ops.Remove(SelectedOp);
        _services.Permissions.SaveOps(ServerDirectory, Ops);
    }

    private void AddWhitelist()
    {
        var name = NewWhitelistName.Trim();
        if (string.IsNullOrWhiteSpace(name))
        {
            return;
        }

        if (Whitelist.Any(o => string.Equals(o.Name, name, StringComparison.OrdinalIgnoreCase)))
        {
            return;
        }

        Whitelist.Add(new WhitelistEntry { Name = name });
        _services.Permissions.SaveWhitelist(ServerDirectory, Whitelist);
        NewWhitelistName = string.Empty;
    }

    private void RemoveWhitelist()
    {
        if (SelectedWhitelist is null)
        {
            return;
        }

        Whitelist.Remove(SelectedWhitelist);
        _services.Permissions.SaveWhitelist(ServerDirectory, Whitelist);
    }

    private static bool IsModdedServerType(string? serverType)
    {
        return string.Equals(serverType, "Forge", StringComparison.OrdinalIgnoreCase)
            || string.Equals(serverType, "Fabric", StringComparison.OrdinalIgnoreCase);
    }

    private void UpdateStats()
    {
        var process = _runtime.Process;
        if (process is null || process.HasExited || Status != ServerStatus.Running)
        {
            CpuUsagePercent = 0;
            MemoryUsageMb = 0;
            _lastCpuCheck = DateTime.MinValue;
            _lastCpuTime = TimeSpan.Zero;
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
        }
        catch
        {
            // Ignore sampling errors.
        }
    }

    private void OnStatusChanged(ServerStatus status)
    {
        Status = status;
    }

    private void OnLogReceived(string message)
    {
        var match = PlayerCountRegex.Match(message);
        if (match.Success && int.TryParse(match.Groups[1].Value, out var count))
        {
            OnlinePlayers = count;
        }
    }

    private bool EnsurePortAvailable()
    {
        var processes = _services.Network.GetProcessesUsingPort(_config.Port);
        if (processes.Count == 0)
        {
            return true;
        }

        var names = string.Join(", ", processes.Select(p => $"{p.ProcessName}({p.Id})"));
        var result = _services.Dialog.Show(
            $"ポート {_config.Port} を使用しているプロセスがあります: {names}\n終了させて続行しますか？",
            "ポート使用中",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning
        );

        if (result != MessageBoxResult.Yes)
        {
            return false;
        }

        if (!_services.Network.TryKillProcessesUsingPort(_config.Port, out var error))
        {
            _services.Dialog.Show(
                $"プロセス終了に失敗しました: {error}",
                "エラー",
                MessageBoxButton.OK,
                MessageBoxImage.Error
            );
            return false;
        }

        return true;
    }

    private async Task TryOpenPortAsync()
    {
        if (_appSettings.PromptUpnp)
        {
            var result = _services.Dialog.Show(
                "ルーターの自動ポート開放を試しますか？",
                "確認",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question
            );
            if (result != MessageBoxResult.Yes)
            {
                return;
            }
        }
        else if (!_appSettings.EnableUpnp)
        {
            return;
        }

        var (ok, error) = await _services.Upnp.TryOpenPortAsync(
            _config.Port,
            $"McServerManager_{_config.Name}"
        );
        if (!ok)
        {
            _services.Dialog.Show(
                error ?? "ポート開放に失敗しました。",
                "警告",
                MessageBoxButton.OK,
                MessageBoxImage.Warning
            );
            return;
        }

        _upnpOpened = true;
    }

    private async Task TryClosePortAsync()
    {
        if (!_upnpOpened)
        {
            return;
        }

        await _services.Upnp.TryClosePortAsync(_config.Port);
        _upnpOpened = false;
    }

    public void Dispose()
    {
        if (_isDisposed)
        {
            return;
        }

        _isDisposed = true;
        _runtime.StatusChanged -= OnStatusChanged;
        _runtime.LogReceived -= OnLogReceived;
        _statsTimer.Stop();
        GC.SuppressFinalize(this);
    }
}
