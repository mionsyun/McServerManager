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
    private bool _eulaAccepted;
    private string _motd = "A Minecraft Server";
    private MinecraftVersionInfo? _selectedVersion;
    private ServerTypeOption? _selectedServerType;
    private string _statusMessage = string.Empty;
    private bool _isBusy;
    private string _progressMessage = string.Empty;
    private double _progressPercent;
    private string _progressStep = string.Empty;
    private bool _hasError;

    public NewServerViewModel(AppServices services, IEnumerable<string> existingNames)
    {
        _services = services;
        _existingNames = new HashSet<string>(existingNames, StringComparer.OrdinalIgnoreCase);
        Versions = new ObservableCollection<MinecraftVersionInfo>();
        ServerTypes = new ObservableCollection<ServerTypeOption>();
        DirectoryPath = _services.Paths.ServersPath;

        BrowseDirectoryCommand = new RelayCommand(_ => BrowseDirectory(), _ => !IsBusy);
        BrowseJavaCommand = new RelayCommand(_ => BrowseJava(), _ => !IsBusy);
        CreateCommand = new AsyncRelayCommand(CreateAsync);
        CancelCommand = new RelayCommand(_ => RequestClose?.Invoke(false), _ => !IsBusy);
        RefreshVersionsCommand = new AsyncRelayCommand(LoadVersionsAsync);

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
    public ObservableCollection<ServerTypeOption> ServerTypes { get; }

    public MinecraftVersionInfo? SelectedVersion
    {
        get => _selectedVersion;
        set => SetProperty(ref _selectedVersion, value);
    }

    public ServerTypeOption? SelectedServerType
    {
        get => _selectedServerType;
        set => SetProperty(ref _selectedServerType, value);
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

    public async Task LoadVersionsAsync()
    {
        IsBusy = true;
        ProgressPercent = 0;
        ProgressStep = "";
        ProgressMessage = "バージョン一覧を取得中...";

        try
        {
            var versions = await _services.Versions.GetVersionsAsync();
            WpfApplication.Current.Dispatcher.Invoke(() =>
            {
                Versions.Clear();
                foreach (var version in versions)
                {
                    Versions.Add(version);
                }
                SelectedVersion = Versions.FirstOrDefault(v => v.Type == "release") ?? Versions.FirstOrDefault();
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

    private void InitializeDefaults()
    {
        JavaPath = _services.Java.FindJavaExecutable() ?? string.Empty;
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
}
