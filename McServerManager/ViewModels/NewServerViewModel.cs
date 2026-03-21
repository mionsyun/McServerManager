using System.Collections.ObjectModel;
using System.IO;
using System.Net.Http;
using System.Windows;
using Forms = System.Windows.Forms;
using OpenFileDialog = Microsoft.Win32.OpenFileDialog;
using WpfApplication = System.Windows.Application;
using McServerManager.Models;
using McServerManager.Services;
using McServerManager.Utilities;

namespace McServerManager.ViewModels;

public sealed class NewServerViewModel : ObservableObject
{
    private readonly AppServices _services;
    private readonly HashSet<string> _existingNames;
    private string _name = string.Empty;
    private string _directoryPath = string.Empty;
    private string _javaPath = string.Empty;
    private int _memoryXms = 1024;
    private int _memoryXmx = 2048;
    private int _port = 25565;
    private int _maxPlayers = 20;
    private bool _onlineMode = true;
    private bool _enableCommandBlock;
    private bool _eulaAccepted;
    private string _motd = "A Minecraft Server";
    private MinecraftVersionInfo? _selectedVersion;
    private ServerTypeOption? _selectedServerType;
    private VersionFilterOption? _selectedVersionFilter;
    private string _statusMessage = string.Empty;
    private string _versionFilterSummary = string.Empty;
    private bool _isBusy;
    private string _progressMessage = string.Empty;
    private double _progressPercent;
    private string _progressStep = string.Empty;
    private bool _hasError;
    private List<MinecraftVersionInfo> _allVersions = [];

    public NewServerViewModel(AppServices services, IEnumerable<string> existingNames)
    {
        _services = services;
        _existingNames = new HashSet<string>(existingNames, StringComparer.OrdinalIgnoreCase);
        Versions = new ObservableCollection<MinecraftVersionInfo>();
        VersionFilters = new ObservableCollection<VersionFilterOption>();
        ServerTypes = new ObservableCollection<ServerTypeOption>();
        DirectoryPath = _services.Paths.ServersPath;

        BrowseDirectoryCommand = new RelayCommand(_ => BrowseDirectory(), _ => !IsBusy);
        BrowseJavaCommand = new RelayCommand(_ => BrowseJava(), _ => !IsBusy);
        CreateCommand = new AsyncRelayCommand(CreateAsync);
        CancelCommand = new RelayCommand(_ => RequestClose?.Invoke(false), _ => !IsBusy);
        RefreshVersionsCommand = new AsyncRelayCommand(
            () => LoadVersionsAsync(forceRefresh: true),
            () => !IsBusy
        );

        InitializeVersionFilters();
        InitializeDefaults();
    }

    public event Action<bool?>? RequestClose;
    public event Action<ServerConfig>? ServerCreated;

    public string Name
    {
        get => _name;
        set => SetProperty(ref _name, value);
    }

    public string DirectoryPath
    {
        get => _directoryPath;
        set => SetProperty(ref _directoryPath, value);
    }

    public string JavaPath
    {
        get => _javaPath;
        set => SetProperty(ref _javaPath, value);
    }

    public int MemoryXms
    {
        get => _memoryXms;
        set => SetProperty(ref _memoryXms, value);
    }

    public int MemoryXmx
    {
        get => _memoryXmx;
        set => SetProperty(ref _memoryXmx, value);
    }

    public int Port
    {
        get => _port;
        set => SetProperty(ref _port, value);
    }

    public int MaxPlayers
    {
        get => _maxPlayers;
        set => SetProperty(ref _maxPlayers, value);
    }

    public bool OnlineMode
    {
        get => _onlineMode;
        set => SetProperty(ref _onlineMode, value);
    }

    public bool EnableCommandBlock
    {
        get => _enableCommandBlock;
        set => SetProperty(ref _enableCommandBlock, value);
    }

    public bool EulaAccepted
    {
        get => _eulaAccepted;
        set => SetProperty(ref _eulaAccepted, value);
    }

    public string Motd
    {
        get => _motd;
        set => SetProperty(ref _motd, value);
    }

    public ObservableCollection<MinecraftVersionInfo> Versions { get; }
    public ObservableCollection<VersionFilterOption> VersionFilters { get; }
    public ObservableCollection<ServerTypeOption> ServerTypes { get; }

    public MinecraftVersionInfo? SelectedVersion
    {
        get => _selectedVersion;
        set
        {
            if (SetProperty(ref _selectedVersion, value))
            {
                OnPropertyChanged(nameof(SelectedVersionInfo));
            }
        }
    }

    public ServerTypeOption? SelectedServerType
    {
        get => _selectedServerType;
        set
        {
            if (SetProperty(ref _selectedServerType, value))
            {
                OnPropertyChanged(nameof(ModVersionHint));
                ApplyVersionFilter(selectCurrentVersion: false);
            }
        }
    }

    public VersionFilterOption? SelectedVersionFilter
    {
        get => _selectedVersionFilter;
        set
        {
            if (SetProperty(ref _selectedVersionFilter, value))
            {
                ApplyVersionFilter(selectCurrentVersion: false);
            }
        }
    }

    public string VersionFilterSummary
    {
        get => _versionFilterSummary;
        private set => SetProperty(ref _versionFilterSummary, value);
    }

    public string SelectedVersionInfo
    {
        get
        {
            if (SelectedVersion is null)
            {
                return "バージョン未選択";
            }

            var typeText = string.Equals(SelectedVersion.Type, "release", StringComparison.OrdinalIgnoreCase)
                ? "正規版"
                : string.Equals(SelectedVersion.Type, "snapshot", StringComparison.OrdinalIgnoreCase)
                    ? "スナップショット"
                    : SelectedVersion.Type;

            return $"{typeText} / {SelectedVersion.ReleaseTime.ToLocalTime():yyyy/MM/dd}";
        }
    }

    public string ModVersionHint
    {
        get
        {
            if (!IsModdedServerType(SelectedServerType?.Id))
            {
                return "Forge/Fabric 以外は用途に応じて選択してください。";
            }

            return "Forge/Fabric は正規版を推奨します。スナップショットは起動しない場合があります。";
        }
    }

    public string StatusMessage
    {
        get => _statusMessage;
        private set => SetProperty(ref _statusMessage, value);
    }

    public bool IsBusy
    {
        get => _isBusy;
        private set
        {
            if (SetProperty(ref _isBusy, value))
            {
                BrowseDirectoryCommand.RaiseCanExecuteChanged();
                BrowseJavaCommand.RaiseCanExecuteChanged();
                CancelCommand.RaiseCanExecuteChanged();
                RefreshVersionsCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public string ProgressMessage
    {
        get => _progressMessage;
        private set => SetProperty(ref _progressMessage, value);
    }

    public double ProgressPercent
    {
        get => _progressPercent;
        private set => SetProperty(ref _progressPercent, value);
    }

    public string ProgressStep
    {
        get => _progressStep;
        private set => SetProperty(ref _progressStep, value);
    }

    public bool HasError
    {
        get => _hasError;
        private set => SetProperty(ref _hasError, value);
    }

    public RelayCommand BrowseDirectoryCommand { get; }
    public RelayCommand BrowseJavaCommand { get; }
    public AsyncRelayCommand CreateCommand { get; }
    public RelayCommand CancelCommand { get; }
    public AsyncRelayCommand RefreshVersionsCommand { get; }

    public async Task LoadVersionsAsync(bool forceRefresh = false)
    {
        IsBusy = true;
        ProgressPercent = 0;
        ProgressStep = "";
        ProgressMessage = "バージョン一覧を取得中...";

        try
        {
            var versions = await _services.Versions.GetVersionsAsync(forceRefresh);
            WpfApplication.Current.Dispatcher.Invoke(() =>
            {
                _allVersions = versions.ToList();
                ApplyVersionFilter(selectCurrentVersion: true);
            });
        }
        catch (Exception ex)
        {
            StatusMessage = $"バージョン取得失敗: {ex.Message}";
            HasError = true;
        }
        finally
        {
            ProgressMessage = string.Empty;
            ProgressPercent = 0;
            ProgressStep = string.Empty;
            IsBusy = false;
        }
    }

    private async Task CreateAsync()
    {
        StatusMessage = string.Empty;
        HasError = false;

        if (!ValidateInput())
        {
            return;
        }

        try
        {
            if (
                IsModdedServerType(SelectedServerType?.Id)
                && SelectedVersion is not null
                && !string.Equals(SelectedVersion.Type, "release", StringComparison.OrdinalIgnoreCase)
            )
            {
                var confirm = _services.Dialog.Show(
                    $"選択中の {SelectedVersion.Id} は {SelectedVersion.Type} です。Forge/Fabric では動作しない可能性があります。続行しますか？",
                    "バージョン確認",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Warning
                );
                if (confirm != MessageBoxResult.Yes)
                {
                    return;
                }
            }

            IsBusy = true;
            ProgressPercent = 0;
            ProgressStep = "0/4";
            ProgressMessage = "サーバー作成を開始しています...";

            var options = new NewServerOptions
            {
                Name = Name.Trim(),
                DirectoryPath = DirectoryPath,
                Type = SelectedServerType?.Id ?? "Vanilla",
                Version = SelectedVersion?.Id ?? "latest",
                MemoryXmsMb = MemoryXms,
                MemoryXmxMb = MemoryXmx,
                Port = Port,
                MaxPlayers = MaxPlayers,
                OnlineMode = OnlineMode,
                EnableCommandBlock = EnableCommandBlock,
                EulaAccepted = EulaAccepted,
                JavaPath = JavaPath,
                Motd = Motd
            };

            var progress = new Progress<string>(message =>
            {
                ProgressMessage = message;
                // ステップに応じてパーセンテージを更新
                if (message.Contains("フォルダを準備")) { ProgressPercent = 10; ProgressStep = "1/4"; }
                else if (message.Contains("取得中") || message.Contains("ダウンロード") || message.Contains("ビルド")) { ProgressPercent = Math.Max(ProgressPercent, 30); ProgressStep = "2/4"; }
                else if (message.Contains("設定ファイル")) { ProgressPercent = 80; ProgressStep = "3/4"; }
                else if (message.Contains("完了")) { ProgressPercent = 100; ProgressStep = "4/4"; }
            });
            var config = await _services.Provisioning.CreateAsync(options, progress);
            ServerCreated?.Invoke(config);
            RequestClose?.Invoke(true);
        }
        catch (HttpRequestException ex)
        {
            HasError = true;
            ProgressStep = "✖";
            StatusMessage = $"ネットワークエラー: {ex.Message}\nインターネット接続を確認してください。";
        }
        catch (IOException ex)
        {
            HasError = true;
            ProgressStep = "✖";
            StatusMessage = $"ファイルエラー: {ex.Message}\n保存先のディスク容量やアクセス権を確認してください。";
        }
        catch (InvalidOperationException ex)
        {
            HasError = true;
            ProgressStep = "✖";
            StatusMessage = $"作成エラー: {ex.Message}";
        }
        catch (Exception ex)
        {
            HasError = true;
            ProgressStep = "✖";
            StatusMessage = $"予期しないエラーが発生しました: {ex.Message}";
        }
        finally
        {
            ProgressMessage = string.Empty;
            ProgressPercent = 0;
            ProgressStep = string.Empty;
            IsBusy = false;
        }
    }

    private void BrowseDirectory()
    {
        using var dialog = new Forms.FolderBrowserDialog
        {
            Description = "サーバー保存先ディレクトリ",
            UseDescriptionForTitle = true,
            SelectedPath = DirectoryPath
        };

        if (dialog.ShowDialog() == Forms.DialogResult.OK)
        {
            DirectoryPath = dialog.SelectedPath;
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

    private void InitializeVersionFilters()
    {
        VersionFilters.Clear();
        VersionFilters.Add(new VersionFilterOption("release", "正規版"));
        VersionFilters.Add(new VersionFilterOption("snapshot", "スナップショット"));
        VersionFilters.Add(new VersionFilterOption("all", "すべて"));
        SelectedVersionFilter = VersionFilters.FirstOrDefault();
    }

    private void ApplyVersionFilter(bool selectCurrentVersion)
    {
        IEnumerable<MinecraftVersionInfo> source = _allVersions;
        var filterId = SelectedVersionFilter?.Id ?? "release";

        source = filterId switch
        {
            "snapshot" => source.Where(v => string.Equals(v.Type, "snapshot", StringComparison.OrdinalIgnoreCase)),
            "release" => source.Where(v => string.Equals(v.Type, "release", StringComparison.OrdinalIgnoreCase)),
            _ => source
        };

        var filtered = source.ToList();
        var preferredId = selectCurrentVersion ? null : SelectedVersion?.Id;
        Versions.Clear();
        foreach (var version in filtered)
        {
            Versions.Add(version);
        }

        SelectedVersion = !string.IsNullOrWhiteSpace(preferredId)
            ? Versions.FirstOrDefault(v => string.Equals(v.Id, preferredId, StringComparison.OrdinalIgnoreCase))
            : null;
        SelectedVersion ??= Versions.FirstOrDefault(v => string.Equals(v.Type, "release", StringComparison.OrdinalIgnoreCase));
        SelectedVersion ??= Versions.FirstOrDefault();

        VersionFilterSummary =
            $"表示 {Versions.Count} 件 / 全体 {_allVersions.Count} 件"
            + (IsModdedServerType(SelectedServerType?.Id) ? "（Forge/Fabric は正規版推奨）" : string.Empty);
    }

    private void InitializeDefaults()
    {
        JavaPath = _services.Java.FindJavaExecutable() ?? string.Empty;
        EnableCommandBlock = false;
        ServerTypes.Clear();
        ServerTypes.Add(new ServerTypeOption("Vanilla", "バニラ"));
        ServerTypes.Add(new ServerTypeOption("Forge", "Forge (MOD)"));
        ServerTypes.Add(new ServerTypeOption("Spigot", "Spigot (プラグイン)"));
        ServerTypes.Add(new ServerTypeOption("Purpur", "Purpur (プラグイン)"));
        ServerTypes.Add(new ServerTypeOption("Paper", "Paper (プラグイン)"));
        ServerTypes.Add(new ServerTypeOption("Fabric", "Fabric (MOD)"));
        SelectedServerType = ServerTypes.FirstOrDefault();
        _ = LoadVersionsAsync();
    }

    private bool ValidateInput()
    {
        if (string.IsNullOrWhiteSpace(Name))
        {
            StatusMessage = "サーバー名は必須です。";
            return false;
        }

        if (SelectedServerType is null)
        {
            StatusMessage = "サーバー種別を選択してください。";
            return false;
        }

        if (SelectedVersion is null)
        {
            StatusMessage = "Minecraft バージョンを選択してください。";
            return false;
        }

        if (_existingNames.Contains(Name.Trim()))
        {
            StatusMessage = "同名のサーバーが既に存在します。";
            return false;
        }

        if (Port is < 1 or > 65535)
        {
            StatusMessage = "ポート番号は1〜65535で入力してください。";
            return false;
        }

        if (MemoryXms <= 0 || MemoryXmx <= 0 || MemoryXms > MemoryXmx)
        {
            StatusMessage = "メモリ割当を確認してください。";
            return false;
        }

        if (!EulaAccepted)
        {
            StatusMessage = "EULAへの同意が必要です。";
            return false;
        }

        return true;
    }

    private static bool IsModdedServerType(string? serverType)
    {
        return string.Equals(serverType, "Forge", StringComparison.OrdinalIgnoreCase)
            || string.Equals(serverType, "Fabric", StringComparison.OrdinalIgnoreCase);
    }
}
