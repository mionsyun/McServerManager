using System.Collections.ObjectModel;
using System.Reflection;
using System.Windows;
using System.Text;
using System.Text.Json;
using WpfApplication = System.Windows.Application;
using McServerManager.Models;
using McServerManager.Services;
using McServerManager.Utilities;
using McServerManager.Views;

namespace McServerManager.ViewModels;

public sealed class MainViewModel : ObservableObject
{
    private static readonly TimeSpan UpdateSnoozeDuration = TimeSpan.FromHours(24);
    private readonly AppServices _services;
    private readonly IBackupSchedulerService _backupScheduler;
    private readonly AppSettings _settings;
    private ServerViewModel? _selectedServer;
    private bool _tutorialOpenedCreate;

    public MainViewModel(AppServices services, IBackupSchedulerService backupScheduler)
    {
        _services = services;
        _backupScheduler = backupScheduler;
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
        CheckAppUpdateCommand = new AsyncRelayCommand(() => CheckForAppUpdateCoreAsync(isManual: true));

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
    public string AppVersion => ResolveAppVersion();

    public ObservableCollection<ServerViewModel> Servers { get; }
    public TutorialViewModel Tutorial { get; }

    private static string ResolveAppVersion()
    {
        var assembly = Assembly.GetExecutingAssembly();
        var informational = assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()
            ?.InformationalVersion;

        if (!string.IsNullOrWhiteSpace(informational))
        {
            return informational.Split('+', 2)[0];
        }

        return assembly.GetName().Version?.ToString() ?? "unknown";
    }
    public ServerViewModel? SelectedServer
    {
        get => _selectedServer;
        set
        {
            if (SetProperty(ref _selectedServer, value))
            {
                // サーバーを切り替えたら必ず「概要」を起点にする(導線の一貫性)
                if (value is not null)
                    value.CurrentView = "overview";
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
    public AsyncRelayCommand CheckAppUpdateCommand { get; }

    public void StartTutorial()
    {
        _tutorialOpenedCreate = false;
        Tutorial.Start(GuideType.InitialSetup);
    }

    public void StartGuide(GuideType type)
    {
        _tutorialOpenedCreate = false;
        Tutorial.Start(type);
    }

    public Task CheckForAppUpdateOnStartupAsync()
    {
        return CheckForAppUpdateCoreAsync(isManual: false);
    }

    private async Task CheckForAppUpdateCoreAsync(bool isManual)
    {
        try
        {
            var checkResult = await _services.AppUpdate.CheckForUpdatesAsync();
            switch (checkResult.Status)
            {
                case AppUpdateCheckStatus.Failed:
                    if (isManual)
                    {
                        _services.Dialog.Show(
                            checkResult.ErrorMessage ?? "更新チェックに失敗しました。",
                            "アプリ更新",
                            MessageBoxButton.OK,
                            MessageBoxImage.Warning);
                    }
                    return;
                case AppUpdateCheckStatus.UpToDate:
                    if (isManual)
                    {
                        _services.Dialog.Show(
                            "現在のアプリは最新です。",
                            "アプリ更新",
                            MessageBoxButton.OK,
                            MessageBoxImage.Information);
                    }
                    return;
                case AppUpdateCheckStatus.UpdateAvailable:
                    break;
            }

            var manifest = checkResult.Manifest;
            if (manifest is null)
            {
                if (isManual)
                {
                    _services.Dialog.Show(
                        "更新情報が不正です。",
                        "アプリ更新",
                        MessageBoxButton.OK,
                        MessageBoxImage.Warning);
                }
                return;
            }

            if (!isManual && IsUpdatePromptDeferred(manifest.Version))
            {
                return;
            }

            await ShowUpdatePromptAndApplyAsync(manifest);
        }
        catch (Exception ex)
        {
            if (isManual)
            {
                _services.Dialog.Show(
                    $"更新チェック中にエラーが発生しました: {ex.Message}",
                    "アプリ更新",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        }
    }

    private async Task ShowUpdatePromptAndApplyAsync(AppUpdateManifest manifest)
    {
        var releaseNotesText = string.IsNullOrWhiteSpace(manifest.ReleaseNotesUrl)
            ? string.Empty
            : $"\n\nリリースノート: {manifest.ReleaseNotesUrl}";
        var prompt = $"新しいバージョン {manifest.Version} が利用可能です。\n今すぐ更新しますか？\n\nいいえを選ぶと24時間後に再通知します。{releaseNotesText}";
        var result = _services.Dialog.Show(prompt, "アプリ更新", MessageBoxButton.YesNo, MessageBoxImage.Information);
        if (result != MessageBoxResult.Yes)
        {
            DeferUpdatePrompt(manifest.Version);
            return;
        }

        if (Servers.Any(server => server.Status != ServerStatus.Stopped))
        {
            _services.Dialog.Show(
                "更新前にすべてのサーバーを停止してください。",
                "アプリ更新",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return;
        }

        var applyResult = await _services.AppUpdate.DownloadAndLaunchInstallerAsync(manifest);
        switch (applyResult.Status)
        {
            case AppUpdateDownloadStatus.Success:
                ClearDeferredUpdatePrompt();
                _services.Dialog.Show(
                    "インストーラーを起動しました。アプリを終了します。",
                    "アプリ更新",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
                WpfApplication.Current?.Shutdown();
                return;
            case AppUpdateDownloadStatus.HashMismatch:
                _services.Dialog.Show(
                    applyResult.ErrorMessage ?? "ダウンロードしたファイルの整合性検証に失敗しました。",
                    "アプリ更新",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
                return;
            case AppUpdateDownloadStatus.SignatureInvalid:
                _services.Dialog.Show(
                    applyResult.ErrorMessage ?? "署名検証に失敗しました。",
                    "アプリ更新",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
                return;
            case AppUpdateDownloadStatus.DownloadFailed:
                _services.Dialog.Show(
                    applyResult.ErrorMessage ?? "更新処理に失敗しました。",
                    "アプリ更新",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
                return;
        }
    }

    private bool IsUpdatePromptDeferred(string version)
    {
        if (string.IsNullOrWhiteSpace(_settings.DeferredAppUpdateVersion)
            || _settings.DeferredAppUpdateUntilUtc is null)
        {
            return false;
        }

        if (!string.Equals(_settings.DeferredAppUpdateVersion, version, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return _settings.DeferredAppUpdateUntilUtc.Value > DateTime.UtcNow;
    }

    private void DeferUpdatePrompt(string version)
    {
        _settings.DeferredAppUpdateVersion = version;
        _settings.DeferredAppUpdateUntilUtc = DateTime.UtcNow.Add(UpdateSnoozeDuration);
        _services.Settings.Save(_settings);
    }

    private void ClearDeferredUpdatePrompt()
    {
        if (_settings.DeferredAppUpdateVersion is null && _settings.DeferredAppUpdateUntilUtc is null)
        {
            return;
        }

        _settings.DeferredAppUpdateVersion = null;
        _settings.DeferredAppUpdateUntilUtc = null;
        _services.Settings.Save(_settings);
    }

    private void OpenTutorialGuideWindow()
    {
        var picker = new GuidePickerWindow
        {
            Owner = WpfApplication.Current.MainWindow
        };

        if (picker.ShowDialog() == true && picker.SelectedGuide.HasValue)
        {
            StartGuide(picker.SelectedGuide.Value);
        }
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
        DisposeServerViewModels(Servers);
        Servers.Clear();
        var configs = _services.Configs.LoadAll(_settings.ServerDirectories);
        foreach (var config in configs.OrderBy(c => c.Name))
        {
            Servers.Add(new ServerViewModel(_services, _backupScheduler, config));
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
            var serverVm = new ServerViewModel(_services, _backupScheduler, config);
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

        var runtimeReleased = _services.Runtime.TryRelease(target.ServerId);
        if (!runtimeReleased)
        {
            _services.Dialog.Show(
                "サーバーランタイムの解放に失敗しました。アプリ再起動で解消する場合があります。",
                "警告",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }

        var directory = target.ServerDirectory;
        if (!string.IsNullOrWhiteSpace(directory) && Directory.Exists(directory))
        {
            if (!TryResolveSafeServerDirectoryForDeletion(directory, target.ServerId, out var safeDirectory, out var validationError))
            {
                _services.Dialog.Show(
                    $"削除を中止しました。{validationError}",
                    "エラー",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
                return;
            }

            try
            {
                ForceDeleteDirectory(safeDirectory);
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

        DisposeServerViewModel(target);
        Servers.Remove(target);
        if (SelectedServer == target)
        {
            SelectedServer = Servers.FirstOrDefault();
        }
    }

    /// <summary>
    /// 読み取り専用属性をすべて解除してからディレクトリを再帰削除する。
    /// Git (BuildTools) が作成する .idx / .pack ファイルなどは
    /// ReadOnly 属性が付いているため、通常の Directory.Delete では失敗する。
    /// </summary>
    private static void ForceDeleteDirectory(string path)
    {
        var root = new DirectoryInfo(path);
        if (!root.Exists)
        {
            return;
        }

        var stack = new Stack<DirectoryInfo>();
        var postOrder = new Stack<DirectoryInfo>();
        stack.Push(root);

        while (stack.Count > 0)
        {
            var current = stack.Pop();
            postOrder.Push(current);

            foreach (var file in current.EnumerateFiles())
            {
                if (file.Attributes.HasFlag(FileAttributes.ReadOnly))
                {
                    file.Attributes = FileAttributes.Normal;
                }

                file.Delete();
            }

            foreach (var directory in current.EnumerateDirectories())
            {
                if (directory.Attributes.HasFlag(FileAttributes.ReparsePoint))
                {
                    if (directory.Attributes.HasFlag(FileAttributes.ReadOnly))
                    {
                        directory.Attributes = FileAttributes.Normal;
                    }

                    directory.Delete();
                    continue;
                }

                stack.Push(directory);
            }
        }

        while (postOrder.Count > 0)
        {
            var directory = postOrder.Pop();
            if (!directory.Exists)
            {
                continue;
            }

            if (directory.Attributes.HasFlag(FileAttributes.ReadOnly))
            {
                directory.Attributes = FileAttributes.Normal;
            }

            directory.Delete();
        }
    }

    private bool TryResolveSafeServerDirectoryForDeletion(
        string directory,
        string expectedServerId,
        out string safeDirectory,
        out string validationError)
    {
        safeDirectory = string.Empty;
        validationError = string.Empty;

        if (string.IsNullOrWhiteSpace(directory))
        {
            validationError = "削除対象ディレクトリが空です。";
            return false;
        }

        string fullDirectory;
        try
        {
            fullDirectory = Path.GetFullPath(directory);
        }
        catch (Exception ex)
        {
            validationError = $"削除対象ディレクトリが不正です: {ex.Message}";
            return false;
        }

        var normalizedDirectory = fullDirectory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        if (string.IsNullOrWhiteSpace(normalizedDirectory))
        {
            validationError = "削除対象ディレクトリが不正です。";
            return false;
        }

        var rootPath = Path.GetPathRoot(normalizedDirectory)?.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) ?? string.Empty;
        if (string.Equals(normalizedDirectory, rootPath, StringComparison.OrdinalIgnoreCase))
        {
            validationError = "ドライブ直下は削除できません。";
            return false;
        }

        var directoryInfo = new DirectoryInfo(normalizedDirectory);
        if (directoryInfo.Attributes.HasFlag(FileAttributes.ReparsePoint))
        {
            validationError = "シンボリックリンク/ジャンクションは削除できません。";
            return false;
        }

        if (!IsUnderManagedServerRoots(normalizedDirectory, out var managedRoot))
        {
            validationError = "管理対象外のディレクトリのため削除できません。";
            return false;
        }

        if (string.Equals(
                normalizedDirectory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
                managedRoot.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
                StringComparison.OrdinalIgnoreCase))
        {
            validationError = "サーバー一覧のルートディレクトリ自体は削除できません。";
            return false;
        }

        var configPath = Path.Combine(normalizedDirectory, "config.json");
        if (!TryReadServerIdFromConfig(configPath, out var configServerId))
        {
            validationError = "config.json の ServerId を確認できません。";
            return false;
        }

        if (!string.Equals(configServerId, expectedServerId, StringComparison.OrdinalIgnoreCase))
        {
            validationError = "config.json の ServerId が選択中サーバーと一致しません。";
            return false;
        }

        safeDirectory = normalizedDirectory;
        return true;
    }

    private bool IsUnderManagedServerRoots(string fullPath, out string matchedRoot)
    {
        matchedRoot = string.Empty;
        foreach (var root in EnumerateManagedServerRoots())
        {
            if (!IsPathUnderRoot(fullPath, root))
            {
                continue;
            }

            matchedRoot = root;
            return true;
        }

        return false;
    }

    private IEnumerable<string> EnumerateManagedServerRoots()
    {
        var roots = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        void AddRoot(string? path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return;
            }

            try
            {
                var normalized = Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                if (!string.IsNullOrWhiteSpace(normalized))
                {
                    roots.Add(normalized);
                }
            }
            catch
            {
                // Ignore invalid paths from settings.
            }
        }

        AddRoot(_services.Paths.ServersPath);
        foreach (var directory in _settings.ServerDirectories)
        {
            AddRoot(directory);
        }

        return roots;
    }

    private static bool IsPathUnderRoot(string path, string root)
    {
        var normalizedPath = Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var normalizedRoot = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        if (string.Equals(normalizedPath, normalizedRoot, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var relative = Path.GetRelativePath(normalizedRoot, normalizedPath);
        if (Path.IsPathRooted(relative))
        {
            return false;
        }

        return relative != ".."
            && !relative.StartsWith($"..{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
            && !relative.StartsWith($"..{Path.AltDirectorySeparatorChar}", StringComparison.Ordinal);
    }

    private static bool TryReadServerIdFromConfig(string configPath, out string serverId)
    {
        serverId = string.Empty;
        if (!File.Exists(configPath))
        {
            return false;
        }

        try
        {
            using var stream = File.OpenRead(configPath);
            using var document = JsonDocument.Parse(stream);
            var root = document.RootElement;

            if (root.TryGetProperty("ServerId", out var serverIdElement) && serverIdElement.ValueKind == JsonValueKind.String)
            {
                serverId = serverIdElement.GetString() ?? string.Empty;
            }
            else if (root.TryGetProperty("serverId", out var camelServerIdElement) && camelServerIdElement.ValueKind == JsonValueKind.String)
            {
                serverId = camelServerIdElement.GetString() ?? string.Empty;
            }

            return !string.IsNullOrWhiteSpace(serverId);
        }
        catch
        {
            return false;
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
        var serverVm = new ServerViewModel(_services, _backupScheduler, config);
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

    private static void DisposeServerViewModels(IEnumerable<ServerViewModel> servers)
    {
        foreach (var server in servers.ToList())
        {
            DisposeServerViewModel(server);
        }
    }

    private static void DisposeServerViewModel(ServerViewModel? server)
    {
        if (server is null)
        {
            return;
        }

        try
        {
            server.Dispose();
        }
        catch
        {
            // Best effort cleanup.
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



