using System.Collections.ObjectModel;
using System.Windows;
using System.Text;
using WpfApplication = System.Windows.Application;
using McServerManager.Models;
using McServerManager.Services;
using McServerManager.Utilities;
using McServerManager.Views;

namespace McServerManager.ViewModels;

public sealed class MainViewModel : ObservableObject
{
    private readonly AppServices _services;
    private readonly AppSettings _settings;
    private ServerViewModel? _selectedServer;
    private bool _tutorialOpenedCreate;

    public MainViewModel(AppServices services)
    {
        _services = services;
        _settings = _services.Settings.Load();

        Servers = new ObservableCollection<ServerViewModel>();

        Tutorial = new TutorialViewModel(_settings, _services.Settings);
        Tutorial.StepChanged += OnTutorialStepChanged;
        StartTutorialCommand = new RelayCommand(_ => OpenTutorialGuideWindow());

        CreateServerCommand = new RelayCommand(_ => OpenCreateServerWindow());
        DeleteServerCommand = new RelayCommand(param => DeleteServer(param as ServerViewModel ?? SelectedServer),
            param => (param as ServerViewModel ?? SelectedServer) is not null);
        DuplicateServerCommand = new RelayCommand(param => DuplicateServer(param as ServerViewModel ?? SelectedServer),
            param => (param as ServerViewModel ?? SelectedServer) is not null);
        RefreshCommand = new RelayCommand(_ => ReloadServers());

        LoadServers();

        _services.Runtime.ServerCrashed += (config, exitCode) => ShowCrashDetails(config, exitCode);
    }

    public bool IsDarkTheme
    {
        get => _services.Theme.IsDark(_settings.Theme);
        set
        {
            var nextTheme = value ? ThemeService.DarkTheme : ThemeService.LightTheme;
            if (!string.Equals(_settings.Theme, nextTheme, StringComparison.OrdinalIgnoreCase))
            {
                _settings.Theme = nextTheme;
                _services.Settings.Save(_settings);
                _services.Theme.Apply(_settings.Theme);
                OnPropertyChanged();
                OnPropertyChanged(nameof(ThemeLabel));
            }
        }
    }

    public string ThemeLabel => IsDarkTheme ? "ダーク" : "ライト";

    public ObservableCollection<ServerViewModel> Servers { get; }
    public TutorialViewModel Tutorial { get; }

    public ServerViewModel? SelectedServer
    {
        get => _selectedServer;
        set
        {
            if (SetProperty(ref _selectedServer, value))
            {
                DeleteServerCommand.RaiseCanExecuteChanged();
                DuplicateServerCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public RelayCommand CreateServerCommand { get; }
    public RelayCommand DeleteServerCommand { get; }
    public RelayCommand DuplicateServerCommand { get; }
    public RelayCommand RefreshCommand { get; }
    public RelayCommand StartTutorialCommand { get; }

    public void StartTutorial()
    {
        _tutorialOpenedCreate = false;
        Tutorial.Start();
    }

    private void OpenTutorialGuideWindow()
    {
        var guide = new FirstRunWindow
        {
            Owner = WpfApplication.Current.MainWindow
        };

        guide.ShowDialog();
        StartTutorial();
    }

    private void OnTutorialStepChanged(object? sender, EventArgs e)
    {
        if (!Tutorial.IsActive)
        {
            _tutorialOpenedCreate = false;
            return;
        }

        if (_tutorialOpenedCreate)
        {
            return;
        }

        if (string.Equals(Tutorial.CurrentTargetName, "NewServerButton", StringComparison.Ordinal))
        {
            _tutorialOpenedCreate = true;
            WpfApplication.Current.Dispatcher.BeginInvoke(OpenCreateServerWindow);
        }
    }

    private void LoadServers()
    {
        Servers.Clear();
        var configs = _services.Configs.LoadAll(_settings.ServerDirectories);
        foreach (var config in configs.OrderBy(c => c.Name))
        {
            Servers.Add(new ServerViewModel(_services, config));
        }

        SelectedServer = Servers.FirstOrDefault();
    }

    private void ReloadServers()
    {
        LoadServers();
    }

    private void OpenCreateServerWindow()
    {
        var window = new NewServerWindow
        {
            Owner = WpfApplication.Current.MainWindow
        };

        var vm = new NewServerViewModel(_services, Servers.Select(s => s.Name));
        vm.RequestClose += result =>
        {
            window.DialogResult = result;
            window.Close();
        };
        vm.ServerCreated += config =>
        {
            TrackServerDirectory(config.DirectoryPath);
            var serverVm = new ServerViewModel(_services, config);
            WpfApplication.Current.Dispatcher.Invoke(() =>
            {
                Servers.Add(serverVm);
                SelectedServer = serverVm;
            });
        };

        window.DataContext = vm;
        window.ShowDialog();
    }

    private void DeleteServer(ServerViewModel? target)
    {
        if (target is null)
        {
            return;
        }

        if (target.Status != ServerStatus.Stopped)
        {
            _services.Dialog.Show("停止中のサーバーのみ削除できます。", "確認", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        if (_services.Dialog.Show("選択したサーバーを削除します。よろしいですか？", "確認", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes)
        {
            return;
        }

        var directory = target.ServerDirectory;
        if (!string.IsNullOrWhiteSpace(directory) && Directory.Exists(directory))
        {
            try
            {
                Directory.Delete(directory, true);
            }
            catch (IOException ex)
            {
                _services.Dialog.Show(
                    $"削除に失敗しました。別のプロセスがファイルを使用中の可能性があります。\n{ex.Message}",
                    "エラー",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
                return;
            }
            catch (UnauthorizedAccessException ex)
            {
                _services.Dialog.Show(
                    $"削除に失敗しました。アクセス権限を確認してください。\n{ex.Message}",
                    "エラー",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
                return;
            }
        }

        Servers.Remove(target);
        if (SelectedServer == target)
        {
            SelectedServer = Servers.FirstOrDefault();
        }
    }

    private void DuplicateServer(ServerViewModel? target)
    {
        if (target is null)
        {
            return;
        }

        if (target.Status != ServerStatus.Stopped)
        {
            _services.Dialog.Show("停止中のサーバーのみ複製できます。", "確認", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var sourceDir = target.ServerDirectory;
        if (string.IsNullOrWhiteSpace(sourceDir) || !Directory.Exists(sourceDir))
        {
            return;
        }

        var newId = Guid.NewGuid().ToString("N");
        var baseDir = Directory.GetParent(sourceDir)?.FullName ?? _services.Paths.ServersPath;
        var targetDir = Path.Combine(baseDir, newId);
        CopyDirectory(sourceDir, targetDir);

        var config = _services.Configs.LoadAll(new[] { targetDir }).FirstOrDefault();
        if (config is null)
        {
            _services.Dialog.Show("複製に失敗しました。", "エラー", MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }

        config.ServerId = newId;
        config.Name = $"{config.Name}_コピー";
        config.CreatedAt = DateTime.UtcNow;
        config.LastStartedAt = null;
        config.DirectoryPath = targetDir;
        config.Firewall = new FirewallRuleInfo();
        _services.Configs.SaveToDirectory(config, targetDir);

        TrackServerDirectory(targetDir);
        var serverVm = new ServerViewModel(_services, config);
        Servers.Add(serverVm);
        SelectedServer = serverVm;
    }

    private void TrackServerDirectory(string serverDirectory)
    {
        var baseDir = Directory.GetParent(serverDirectory)?.FullName ?? string.Empty;
        if (string.IsNullOrWhiteSpace(baseDir))
        {
            return;
        }

        if (string.Equals(baseDir.TrimEnd(Path.DirectorySeparatorChar), _services.Paths.ServersPath.TrimEnd(Path.DirectorySeparatorChar), StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        if (!_settings.ServerDirectories.Contains(baseDir, StringComparer.OrdinalIgnoreCase))
        {
            _settings.ServerDirectories.Add(baseDir);
            _services.Settings.Save(_settings);
        }
    }

    private static void CopyDirectory(string source, string destination)
    {
        Directory.CreateDirectory(destination);
        foreach (var file in Directory.GetFiles(source))
        {
            var targetFile = Path.Combine(destination, Path.GetFileName(file));
            File.Copy(file, targetFile, true);
        }

        foreach (var dir in Directory.GetDirectories(source))
        {
            var targetDir = Path.Combine(destination, Path.GetFileName(dir));
            CopyDirectory(dir, targetDir);
        }
    }

    private void ShowCrashDetails(ServerConfig config, int? exitCode)
    {
        var serverVm = Servers.FirstOrDefault(s => string.Equals(s.ServerId, config.ServerId, StringComparison.OrdinalIgnoreCase));
        var serverName = serverVm?.Name ?? config.Name ?? "Unknown";
        var logs = serverVm?.Logs?.TakeLast(8).ToArray() ?? Array.Empty<string>();
        var logsText = logs.Length == 0 ? "(ログなし)" : string.Join(Environment.NewLine, logs);
        var logsDir = Path.Combine(config.DirectoryPath, "logs");
        var crashReportsDir = Path.Combine(config.DirectoryPath, "crash-reports");

        var message = new StringBuilder()
            .AppendLine("サーバーが予期せず終了しました。")
            .AppendLine($"サーバー: {serverName}")
            .AppendLine($"ExitCode: {exitCode?.ToString() ?? "-"}")
            .AppendLine($"ログ: {logsDir}")
            .AppendLine($"クラッシュレポート: {crashReportsDir}")
            .AppendLine("確認: コンソールタブの「ログフォルダ」「クラッシュレポート」から最新ファイルを開いてください。")
            .AppendLine("復旧のヒント: Javaバージョン/プラグイン・MOD/ポート競合を確認してください。")
            .AppendLine("直近ログ:")
            .AppendLine(logsText)
            .ToString();

        _services.Dialog.Show(message, "警告", MessageBoxButton.OK, MessageBoxImage.Warning);
    }
}
