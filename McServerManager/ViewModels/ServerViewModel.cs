using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Threading;
using OpenFileDialog = Microsoft.Win32.OpenFileDialog;
using WpfApplication = System.Windows.Application;
using WpfMessageBox = System.Windows.MessageBox;
using McServerManager.Models;
using McServerManager.Services;
using McServerManager.Utilities;

namespace McServerManager.ViewModels;

public sealed class ServerViewModel : ObservableObject
{
    private static readonly Regex PlayerCountRegex = new(@"There are (\d+) of a max of (\d+) players online", RegexOptions.Compiled);
    private readonly AppServices _services;
    private readonly ServerRuntime _runtime;
    private readonly AppSettings _appSettings;
    private readonly DispatcherTimer _statsTimer;
    private ServerConfig _config;
    private ServerSettingsViewModel _settings;
    private string _commandText = string.Empty;
    private bool _restartRequired;
    private string _javaPath = string.Empty;
    private string _localPortStatus = "未チェック";
    private string _publicIp = "-";
    private string _publicIpStatus = string.Empty;
    private string _newWorldName = string.Empty;
    private string _backupComment = string.Empty;
    private bool _backupCurrentBeforeRestore = true;
    private MinecraftVersionInfo? _selectedVersion;
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
    private LaunchModeOption? _selectedLaunchMode;
    private StartupPresetOption? _selectedStartupPreset;
    private string _javaExtraArguments = string.Empty;
    private int _memoryXmsMb;
    private int _memoryXmxMb;

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
        Worlds = new ObservableCollection<string>();
        Backups = new ObservableCollection<BackupMetadata>();
        AvailableVersions = new ObservableCollection<MinecraftVersionInfo>();
        LanIpAddresses = new ObservableCollection<string>(_services.Network.GetLanIpAddresses());
        ExternalChecklist = new ObservableCollection<string>(_services.Network.GetExternalChecklist());
        Ops = new ObservableCollection<OpEntry>();
        Whitelist = new ObservableCollection<WhitelistEntry>();
        Addons = new ObservableCollection<AddonEntry>();
        LaunchModes = new ObservableCollection<LaunchModeOption>();
        StartupPresets = new ObservableCollection<StartupPresetOption>();

        AutoRestartOnCrash = _config.AutoRestartOnCrash;
        AutoRestartDelaySeconds = _config.AutoRestartDelaySeconds;
        JavaPath = _config.JavaPath;
        _memoryXmsMb = _config.MemoryXmsMb;
        _memoryXmxMb = _config.MemoryXmxMb;
        _javaExtraArguments = _config.JavaExtraArguments ?? string.Empty;
        InitializeLaunchOptions();

        StartCommand = new AsyncRelayCommand(StartAsync, () => Status == ServerStatus.Stopped);
        StopCommand = new AsyncRelayCommand(StopAsync, () => Status != ServerStatus.Stopped);
        RestartCommand = new AsyncRelayCommand(RestartAsync, () => Status == ServerStatus.Running);
        SendCommandCommand = new RelayCommand(_ => SendCommand(), _ => Status == ServerStatus.Running);
        ExportLogsCommand = new RelayCommand(_ => ExportLogs());
        SaveSettingsCommand = new RelayCommand(_ => SaveSettings());
        BrowseJavaCommand = new RelayCommand(_ => BrowseJava());
        SwitchWorldCommand = new RelayCommand(_ => SwitchWorld(), _ => !string.IsNullOrWhiteSpace(SelectedWorld) && !IsSelectedWorldCurrent);
        DeleteWorldCommand = new RelayCommand(_ => DeleteWorld(), _ => !string.IsNullOrWhiteSpace(SelectedWorld));
        CreateWorldCommand = new RelayCommand(_ => CreateWorld(), _ => !string.IsNullOrWhiteSpace(NewWorldName));
        BackupWorldCommand = new RelayCommand(_ => BackupWorld(), _ => !string.IsNullOrWhiteSpace(SelectedWorld));
        RestoreWorldCommand = new RelayCommand(_ => RestoreWorld(), _ => SelectedBackup is not null);
        RefreshBackupsCommand = new RelayCommand(_ => LoadBackups());
        RefreshWorldsCommand = new RelayCommand(_ => LoadWorlds());
        OpenSelectedWorldDirectoryCommand = new RelayCommand(_ => OpenSelectedWorldDirectory(), _ => !string.IsNullOrWhiteSpace(SelectedWorld));
        OpenServerDirectoryCommand = new RelayCommand(_ => OpenServerDirectory());
        OpenLogsDirectoryCommand = new RelayCommand(_ => OpenLogsDirectory());
        OpenCrashReportsCommand = new RelayCommand(_ => OpenCrashReports());
        LoadVersionsCommand = new AsyncRelayCommand(LoadVersionsAsync);
        ChangeVersionCommand = new AsyncRelayCommand(ChangeVersionAsync, () => SelectedVersion is not null);
        RedownloadJarCommand = new AsyncRelayCommand(RedownloadJarAsync);
        CreateFirewallRuleCommand = new RelayCommand(_ => CreateFirewallRule());
        DeleteFirewallRuleCommand = new RelayCommand(_ => DeleteFirewallRule());
        RecreateFirewallRuleCommand = new RelayCommand(_ => RecreateFirewallRule());
        CheckPortCommand = new AsyncRelayCommand(CheckPortAsync);
        RefreshPublicIpCommand = new AsyncRelayCommand(RefreshPublicIpAsync);
        AddOpCommand = new RelayCommand(_ => AddOp(), _ => !string.IsNullOrWhiteSpace(NewOpName));
        RemoveOpCommand = new RelayCommand(_ => RemoveOp(), _ => SelectedOp is not null);
        AddWhitelistCommand = new RelayCommand(_ => AddWhitelist(), _ => !string.IsNullOrWhiteSpace(NewWhitelistName));
        RemoveWhitelistCommand = new RelayCommand(_ => RemoveWhitelist(), _ => SelectedWhitelist is not null);
        ReloadPermissionsCommand = new RelayCommand(_ => LoadPermissions());
        AddAddonsCommand = new RelayCommand(_ => BrowseAndAddAddons(), _ => SupportsAddonManagement);
        RefreshAddonsCommand = new RelayCommand(_ => LoadAddons(), _ => SupportsAddonManagement);
        OpenAddonsDirectoryCommand = new RelayCommand(_ => OpenAddonsDirectory(), _ => SupportsAddonManagement);
        EnableAddonCommand = new RelayCommand(_ => EnableAddon(), _ => SelectedAddon is not null && !SelectedAddon.IsEnabled);
        DisableAddonCommand = new RelayCommand(_ => DisableAddon(), _ => SelectedAddon is not null && SelectedAddon.IsEnabled);
        DeleteAddonCommand = new RelayCommand(_ => DeleteAddon(), _ => SelectedAddon is not null);
        ApplyStartupPresetCommand = new RelayCommand(_ => ApplyStartupPreset(), _ => SelectedStartupPreset is not null);
        RunHealthCheckCommand = new RelayCommand(_ => RunHealthCheck(showEvenIfCompleted: true));

        LoadSettings();
        LoadWorlds();
        LoadBackups();
        LoadPermissions();
        LoadAddons();
        _ = LoadVersionsAsync();

        _statsTimer = new DispatcherTimer(TimeSpan.FromSeconds(1), DispatcherPriority.Background, (_, _) => UpdateStats(), WpfApplication.Current.Dispatcher);
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
    public ObservableCollection<BackupMetadata> Backups { get; }
    public ObservableCollection<MinecraftVersionInfo> AvailableVersions { get; }
    public ObservableCollection<string> LanIpAddresses { get; }
    public ObservableCollection<string> ExternalChecklist { get; }
    public ObservableCollection<OpEntry> Ops { get; }
    public ObservableCollection<WhitelistEntry> Whitelist { get; }
    public ObservableCollection<AddonEntry> Addons { get; }
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
            }
        }
    }

    public string StatusText => Status switch
    {
        ServerStatus.Starting => "起動中",
        ServerStatus.Running => "稼働中",
        ServerStatus.Stopping => "停止中",
        _ => "停止"
    };

    public string LastStartedAtText
        => _config.LastStartedAt?.ToLocalTime().ToString("yyyy/MM/dd HH:mm") ?? "-";

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

    public string LocalPortStatus
    {
        get => _localPortStatus;
        private set => SetProperty(ref _localPortStatus, value);
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
                BackupWorldCommand.RaiseCanExecuteChanged();
                OpenSelectedWorldDirectoryCommand.RaiseCanExecuteChanged();
                OnPropertyChanged(nameof(IsSelectedWorldCurrent));
                OnPropertyChanged(nameof(WorldSwitchHint));
            }
        }
    }

    public string CurrentWorldName => string.IsNullOrWhiteSpace(_config.WorldName) ? "-" : _config.WorldName;

    public bool IsSelectedWorldCurrent
        => !string.IsNullOrWhiteSpace(SelectedWorld)
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

    private BackupMetadata? _selectedBackup;
    public BackupMetadata? SelectedBackup
    {
        get => _selectedBackup;
        set
        {
            if (SetProperty(ref _selectedBackup, value))
            {
                RestoreWorldCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public string BackupComment
    {
        get => _backupComment;
        set => SetProperty(ref _backupComment, value);
    }

    public bool BackupCurrentBeforeRestore
    {
        get => _backupCurrentBeforeRestore;
        set => SetProperty(ref _backupCurrentBeforeRestore, value);
    }

    public MinecraftVersionInfo? SelectedVersion
    {
        get => _selectedVersion;
        set
        {
            if (SetProperty(ref _selectedVersion, value))
            {
                ChangeVersionCommand.RaiseCanExecuteChanged();
            }
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
    public RelayCommand SwitchWorldCommand { get; }
    public RelayCommand DeleteWorldCommand { get; }
    public RelayCommand CreateWorldCommand { get; }
    public RelayCommand BackupWorldCommand { get; }
    public RelayCommand RestoreWorldCommand { get; }
    public RelayCommand RefreshBackupsCommand { get; }
    public RelayCommand RefreshWorldsCommand { get; }
    public RelayCommand OpenSelectedWorldDirectoryCommand { get; }
    public RelayCommand OpenServerDirectoryCommand { get; }
    public RelayCommand OpenLogsDirectoryCommand { get; }
    public RelayCommand OpenCrashReportsCommand { get; }
    public AsyncRelayCommand LoadVersionsCommand { get; }
    public AsyncRelayCommand ChangeVersionCommand { get; }
    public AsyncRelayCommand RedownloadJarCommand { get; }
    public RelayCommand CreateFirewallRuleCommand { get; }
    public RelayCommand DeleteFirewallRuleCommand { get; }
    public RelayCommand RecreateFirewallRuleCommand { get; }
    public AsyncRelayCommand CheckPortCommand { get; }
    public AsyncRelayCommand RefreshPublicIpCommand { get; }
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

        _selectedLaunchMode = LaunchModes.FirstOrDefault(item => string.Equals(item.Id, _config.LaunchModeOverride, StringComparison.OrdinalIgnoreCase))
            ?? LaunchModes.FirstOrDefault(item => string.Equals(item.Id, "Auto", StringComparison.OrdinalIgnoreCase));
        OnPropertyChanged(nameof(SelectedLaunchMode));
        OnPropertyChanged(nameof(LaunchModeDescription));

        _selectedStartupPreset = StartupPresets.FirstOrDefault(item => string.Equals(item.Id, _config.StartupPresetId, StringComparison.OrdinalIgnoreCase))
            ?? StartupPresets.FirstOrDefault(item => string.Equals(item.Id, "Balanced", StringComparison.OrdinalIgnoreCase));
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
        WpfMessageBox.Show($"起動プリセット「{SelectedStartupPreset.Label}」を適用しました。", "完了", MessageBoxButton.OK, MessageBoxImage.Information);
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
            WpfMessageBox.Show("サーバーディレクトリが見つかりません。", "エラー", MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }

        if (!EnsurePortAvailable())
        {
            return;
        }

        await TryOpenPortAsync();

        try
        {
            _config.LastStartedAt = DateTime.UtcNow;
            _services.Configs.Save(_config);
            OnPropertyChanged(nameof(LastStartedAtText));
            await _services.Runtime.StartAsync(_config, ServerDirectory);
        }
        catch (Exception ex)
        {
            WpfMessageBox.Show($"起動に失敗しました: {ex.Message}", "エラー", MessageBoxButton.OK, MessageBoxImage.Error);
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
            var hasWinArgs = Directory.Exists(ServerDirectory)
                && Directory.EnumerateFiles(ServerDirectory, "win_args.txt", SearchOption.AllDirectories).Any();
            if (!hasRunBat && !hasWinArgs)
            {
                issues.Add("Forge の起動補助ファイル (run.bat / win_args.txt) が見つかりません。\nバージョン再ダウンロードまたは Forge の再インストールを推奨します。");
            }
        }

        _config.HasCompletedInitialHealthCheck = true;
        _services.Configs.Save(_config);

        if (issues.Count == 0 && fixes.Count == 0)
        {
            if (showEvenIfCompleted)
            {
                WpfMessageBox.Show("ヘルスチェックで問題は見つかりませんでした。", "ヘルスチェック", MessageBoxButton.OK, MessageBoxImage.Information);
            }

            return;
        }

        var message = string.Join(Environment.NewLine + Environment.NewLine, issues.Concat(fixes));
        var image = issues.Count > 0 ? MessageBoxImage.Warning : MessageBoxImage.Information;
        WpfMessageBox.Show(message, "初回ヘルスチェック", MessageBoxButton.OK, image);
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

        if (!_services.Java.TryGetJavaMajorVersion(javaExe, out var actualMajor, out var rawVersion))
        {
            return;
        }

        if (actualMajor >= requiredMajor)
        {
            return;
        }

        var versionText = string.IsNullOrWhiteSpace(rawVersion) ? actualMajor.ToString() : rawVersion;
        WpfMessageBox.Show(
            $"Minecraft {Version} は Java {requiredMajor} 以上が推奨です。現在の Java: {versionText}\n必要に応じて「設定」タブで java.exe を切り替えてください。",
            "Java バージョン警告",
            MessageBoxButton.OK,
            MessageBoxImage.Warning);
    }

    private async Task StopAsync()
    {
        try
        {
            await _services.Runtime.StopAsync(_config);
        }
        catch (Exception ex)
        {
            WpfMessageBox.Show($"停止に失敗しました: {ex.Message}", "エラー", MessageBoxButton.OK, MessageBoxImage.Error);
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
            WpfMessageBox.Show($"再起動に失敗しました: {ex.Message}", "エラー", MessageBoxButton.OK, MessageBoxImage.Error);
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
            WpfMessageBox.Show($"ログを保存しました: {path}", "完了", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            WpfMessageBox.Show($"ログ出力に失敗しました: {ex.Message}", "エラー", MessageBoxButton.OK, MessageBoxImage.Error);
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
            WpfMessageBox.Show($"設定保存に失敗しました: {ex.Message}", "エラー", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void BrowseJava()
    {
        var dialog = new OpenFileDialog
        {
            Filter = "java.exe|java.exe",
            FileName = "java.exe"
        };

        if (dialog.ShowDialog() == true)
        {
            JavaPath = dialog.FileName;
        }
    }

    private void LoadWorlds()
    {
        Worlds.Clear();
        foreach (var world in _services.Worlds.GetWorlds(ServerDirectory))
        {
            Worlds.Add(world);
        }

        SelectedWorld = Worlds.FirstOrDefault(w => string.Equals(w, _config.WorldName, StringComparison.OrdinalIgnoreCase))
            ?? Worlds.FirstOrDefault();
        OnPropertyChanged(nameof(CurrentWorldName));
        OnPropertyChanged(nameof(IsSelectedWorldCurrent));
        OnPropertyChanged(nameof(WorldSwitchHint));
        SwitchWorldCommand.RaiseCanExecuteChanged();
        OpenSelectedWorldDirectoryCommand.RaiseCanExecuteChanged();
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
                WpfMessageBox.Show(missingMessage, "エラー", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            Process.Start(new ProcessStartInfo
            {
                FileName = path,
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            WpfMessageBox.Show($"フォルダを開けませんでした: {ex.Message}", "エラー", MessageBoxButton.OK, MessageBoxImage.Error);
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
            WpfMessageBox.Show($"ワールド作成に失敗しました: {ex.Message}", "エラー", MessageBoxButton.OK, MessageBoxImage.Error);
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
            WpfMessageBox.Show("停止中のみ削除できます。", "確認", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        if (WpfMessageBox.Show($"ワールド {SelectedWorld} を削除します。", "確認", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes)
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
            WpfMessageBox.Show($"削除に失敗しました: {ex.Message}", "エラー", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void LoadBackups()
    {
        Backups.Clear();
        var backupDir = Path.Combine(ServerDirectory, "backups");
        foreach (var backup in _services.Worlds.GetBackups(backupDir))
        {
            Backups.Add(backup);
        }
    }

    private void BackupWorld()
    {
        if (string.IsNullOrWhiteSpace(SelectedWorld))
        {
            return;
        }

        try
        {
            var backupsDir = Path.Combine(ServerDirectory, "backups");
            _services.Worlds.BackupWorld(ServerDirectory, SelectedWorld, backupsDir, BackupComment);
            BackupComment = string.Empty;
            LoadBackups();
        }
        catch (Exception ex)
        {
            WpfMessageBox.Show($"バックアップに失敗しました: {ex.Message}", "エラー", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void RestoreWorld()
    {
        if (SelectedBackup is null)
        {
            return;
        }

        if (Status != ServerStatus.Stopped)
        {
            WpfMessageBox.Show("停止中のみ復元できます。", "確認", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var targetWorld = SelectedWorld ?? _config.WorldName;
        if (string.IsNullOrWhiteSpace(targetWorld))
        {
            WpfMessageBox.Show("復元先のワールドを選択してください。", "確認", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        try
        {
            if (BackupCurrentBeforeRestore && !string.IsNullOrWhiteSpace(_config.WorldName))
            {
                var backupsDir = Path.Combine(ServerDirectory, "backups");
                _services.Worlds.BackupWorld(ServerDirectory, _config.WorldName, backupsDir, "restore");
            }

            var backupsDirectory = Path.Combine(ServerDirectory, "backups");
            _services.Worlds.RestoreBackup(ServerDirectory, backupsDirectory, SelectedBackup, targetWorld);
            LoadWorlds();
            OnPropertyChanged(nameof(WorldSwitchHint));
        }
        catch (Exception ex)
        {
            WpfMessageBox.Show($"復元に失敗しました: {ex.Message}", "エラー", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private async Task LoadVersionsAsync()
    {
        try
        {
            var versions = await _services.Versions.GetVersionsAsync();
            WpfApplication.Current.Dispatcher.Invoke(() =>
            {
                AvailableVersions.Clear();
                foreach (var version in versions)
                {
                    AvailableVersions.Add(version);
                }
                SelectedVersion = AvailableVersions.FirstOrDefault(v => string.Equals(v.Id, _config.Version, StringComparison.OrdinalIgnoreCase))
                    ?? AvailableVersions.FirstOrDefault(v => v.Type == "release")
                    ?? AvailableVersions.FirstOrDefault();
            });
        }
        catch (Exception ex)
        {
            WpfMessageBox.Show($"バージョン取得に失敗しました: {ex.Message}", "エラー", MessageBoxButton.OK, MessageBoxImage.Error);
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
            WpfMessageBox.Show("停止中のみ変更できます。", "確認", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        if (WpfMessageBox.Show($"バージョンを {SelectedVersion.Id} に変更します。", "確認", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
        {
            return;
        }

        try
        {
            if (!string.IsNullOrWhiteSpace(_config.WorldName))
            {
                var backupsDir = Path.Combine(ServerDirectory, "backups");
                _services.Worlds.BackupWorld(ServerDirectory, _config.WorldName, backupsDir, "version-change");
            }

            var jarPath = Path.Combine(ServerDirectory, "server.jar");
            await _services.Jars.DownloadAsync(_config.Type, SelectedVersion.Id, jarPath, _config.JavaPath);
            _config.Version = SelectedVersion.Id;
            _services.Configs.Save(_config);
            OnPropertyChanged(nameof(Version));
        }
        catch (Exception ex)
        {
            WpfMessageBox.Show($"バージョン変更に失敗しました: {ex.Message}", "エラー", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private async Task RedownloadJarAsync()
    {
        if (Status != ServerStatus.Stopped)
        {
            WpfMessageBox.Show("停止中のみ再ダウンロードできます。", "確認", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        try
        {
            var jarPath = Path.Combine(ServerDirectory, "server.jar");
            await _services.Jars.DownloadAsync(_config.Type, _config.Version, jarPath, _config.JavaPath);
        }
        catch (Exception ex)
        {
            WpfMessageBox.Show($"再ダウンロードに失敗しました: {ex.Message}", "エラー", MessageBoxButton.OK, MessageBoxImage.Error);
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
            WpfMessageBox.Show("Firewall ルールを作成しました。", "完了", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            WpfMessageBox.Show($"Firewall ルール作成に失敗しました: {ex.Message}", "エラー", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void DeleteFirewallRule()
    {
        try
        {
            _services.Firewall.DeleteRules(_config.Firewall);
            WpfMessageBox.Show("Firewall ルールを削除しました。", "完了", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            WpfMessageBox.Show($"Firewall ルール削除に失敗しました: {ex.Message}", "エラー", MessageBoxButton.OK, MessageBoxImage.Error);
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
            WpfMessageBox.Show("Firewall ルールを再作成しました。", "完了", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            WpfMessageBox.Show($"Firewall ルール再作成に失敗しました: {ex.Message}", "エラー", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private async Task CheckPortAsync()
    {
        LocalPortStatus = "確認中...";
        var isOpen = await _services.Network.IsLocalPortOpenAsync(_config.Port);
        LocalPortStatus = isOpen ? "待受中" : "未待受";
    }

    private async Task RefreshPublicIpAsync()
    {
        PublicIpStatus = "取得中...";
        var ip = await _services.Network.GetPublicIpAsync();
        PublicIp = string.IsNullOrWhiteSpace(ip) ? "-" : ip;
        PublicIpStatus = string.IsNullOrWhiteSpace(ip) ? "取得失敗" : string.Empty;
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
            RaiseAddonCommandsCanExecuteChanged();
            OnPropertyChanged(nameof(SupportsAddonManagement));
            OnPropertyChanged(nameof(AddonCategoryName));
            OnPropertyChanged(nameof(AddonActiveDirectory));
            OnPropertyChanged(nameof(AddonDisabledDirectory));
            return;
        }

        try
        {
            foreach (var addon in _services.Addons.GetAddons(ServerDirectory, ServerType))
            {
                Addons.Add(addon);
            }

            AddonStatus = Addons.Count == 0
                ? $"{AddonCategoryName}ファイルがまだありません。"
                : $"{AddonCategoryName} {Addons.Count} 件";

            var warnings = _services.Addons.AnalyzeCompatibilityWarnings(ServerType, Addons);
            if (warnings.Count > 0)
            {
                AddonStatus += $" / 警告 {warnings.Count} 件";
            }
        }
        catch (Exception ex)
        {
            AddonStatus = "一覧取得に失敗しました。";
            WpfMessageBox.Show($"一覧取得に失敗しました: {ex.Message}", "エラー", MessageBoxButton.OK, MessageBoxImage.Error);
        }

        RaiseAddonCommandsCanExecuteChanged();
        OnPropertyChanged(nameof(SupportsAddonManagement));
        OnPropertyChanged(nameof(AddonCategoryName));
        OnPropertyChanged(nameof(AddonActiveDirectory));
        OnPropertyChanged(nameof(AddonDisabledDirectory));
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
            CheckFileExists = true
        };

        if (dialog.ShowDialog() == true)
        {
            ImportAddons(dialog.FileNames);
        }
    }

    public void ImportAddons(IEnumerable<string> sourcePaths)
    {
        if (!SupportsAddonManagement)
        {
            return;
        }

        try
        {
            var count = _services.Addons.AddFiles(ServerDirectory, ServerType, sourcePaths);
            if (count > 0)
            {
                AddonStatus = $"{count} 件を追加しました。";
            }
            else
            {
                AddonStatus = "追加できるjarファイルが見つかりませんでした。";
            }

            LoadAddons();
            ShowAddonCompatibilityWarnings();
        }
        catch (Exception ex)
        {
            WpfMessageBox.Show($"追加に失敗しました: {ex.Message}", "エラー", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void ShowAddonCompatibilityWarnings()
    {
        var warnings = _services.Addons.AnalyzeCompatibilityWarnings(ServerType, Addons);
        if (warnings.Count == 0)
        {
            return;
        }

        var lines = string.Join(Environment.NewLine, warnings.Select((warning, index) => $"{index + 1}. {warning}"));
        WpfMessageBox.Show(
            $"追加した {AddonCategoryName} に互換性の注意点があります。{Environment.NewLine}{Environment.NewLine}{lines}",
            "互換性チェック",
            MessageBoxButton.OK,
            MessageBoxImage.Warning);
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
            WpfMessageBox.Show($"無効化に失敗しました: {ex.Message}", "エラー", MessageBoxButton.OK, MessageBoxImage.Error);
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
            WpfMessageBox.Show($"有効化に失敗しました: {ex.Message}", "エラー", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void DeleteAddon()
    {
        if (SelectedAddon is null)
        {
            return;
        }

        if (WpfMessageBox.Show($"{SelectedAddon.FileName} を削除します。", "確認", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes)
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
            WpfMessageBox.Show($"削除に失敗しました: {ex.Message}", "エラー", MessageBoxButton.OK, MessageBoxImage.Error);
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

    private void RaiseAddonCommandsCanExecuteChanged()
    {
        AddAddonsCommand.RaiseCanExecuteChanged();
        RefreshAddonsCommand.RaiseCanExecuteChanged();
        OpenAddonsDirectoryCommand.RaiseCanExecuteChanged();
        EnableAddonCommand.RaiseCanExecuteChanged();
        DisableAddonCommand.RaiseCanExecuteChanged();
        DeleteAddonCommand.RaiseCanExecuteChanged();
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
            var now = DateTime.UtcNow;
            var totalCpu = process.TotalProcessorTime;
            if (_lastCpuCheck != DateTime.MinValue)
            {
                var deltaCpu = totalCpu - _lastCpuTime;
                var deltaTime = now - _lastCpuCheck;
                var cpuPercent = deltaTime.TotalMilliseconds > 0
                    ? deltaCpu.TotalMilliseconds / (deltaTime.TotalMilliseconds * Environment.ProcessorCount) * 100
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
        var result = WpfMessageBox.Show(
            $"ポート {_config.Port} を使用しているプロセスがあります: {names}\n終了させて続行しますか？",
            "ポート使用中",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);

        if (result != MessageBoxResult.Yes)
        {
            return false;
        }

        if (!_services.Network.TryKillProcessesUsingPort(_config.Port, out var error))
        {
            WpfMessageBox.Show($"プロセス終了に失敗しました: {error}", "エラー", MessageBoxButton.OK, MessageBoxImage.Error);
            return false;
        }

        return true;
    }

    private async Task TryOpenPortAsync()
    {
        if (_appSettings.PromptUpnp)
        {
            var result = WpfMessageBox.Show("ルーターの自動ポート開放を試しますか？", "確認", MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (result != MessageBoxResult.Yes)
            {
                return;
            }
        }
        else if (!_appSettings.EnableUpnp)
        {
            return;
        }

        var (ok, error) = await _services.Upnp.TryOpenPortAsync(_config.Port, $"McServerManager_{_config.Name}");
        if (!ok)
        {
            WpfMessageBox.Show($"ポート開放に失敗しました: {error}", "警告", MessageBoxButton.OK, MessageBoxImage.Warning);
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
}
