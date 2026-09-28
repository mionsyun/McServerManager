using System.Net.Http;
using System.Windows;
using McServerManager.Models;
using McServerManager.Services;
using McServerManager.Utilities;
using OpenFileDialog = Microsoft.Win32.OpenFileDialog;

namespace McServerManager.ViewModels;

/// <summary>統合版 (BDS) 本体の更新。公式の最新版、または手元の zip から入れ替える。</summary>
public sealed class BedrockUpdateViewModel : ObservableObject
{
    private readonly IBedrockServerService _bedrockServer;
    private readonly IWorldService _worlds;
    private readonly IServerConfigService _configs;
    private readonly IDialogService _dialog;
    private readonly ServerConfig _config;
    private readonly Func<ServerStatus> _getStatus;

    private BedrockVersionInfo? _latestRelease;
    private BedrockVersionInfo? _latestPreview;
    private bool _usePreviewChannel;
    private bool _isBusy;
    private string _versionStatus = "「最新版を確認」を押すと公式サイトの最新版を調べます。";
    private string _progressMessage = string.Empty;

    public BedrockUpdateViewModel(
        IBedrockServerService bedrockServer,
        IWorldService worlds,
        IServerConfigService configs,
        IDialogService dialog,
        ServerConfig config,
        Func<ServerStatus> getStatus)
    {
        _bedrockServer = bedrockServer;
        _worlds = worlds;
        _configs = configs;
        _dialog = dialog;
        _config = config;
        _getStatus = getStatus;

        CheckForUpdateCommand = new AsyncRelayCommand(CheckForUpdateAsync, () => !IsBusy);
        UpdateCommand = new AsyncRelayCommand(UpdateFromOfficialAsync, () => !IsBusy && TargetVersion is not null);
        UpdateFromZipCommand = new AsyncRelayCommand(UpdateFromZipAsync, () => !IsBusy);
    }

    /// <summary>更新完了後に通知（通信方式の既定がバージョンで変わるため、接続情報の再計算に使う）。</summary>
    public event Action? Updated;

    public string InstalledVersion => string.IsNullOrWhiteSpace(_config.Version) ? "不明" : _config.Version;

    public bool UsePreviewChannel
    {
        get => _usePreviewChannel;
        set
        {
            if (SetProperty(ref _usePreviewChannel, value))
                RefreshVersionState();
        }
    }

    public BedrockVersionInfo? TargetVersion => UsePreviewChannel ? _latestPreview : _latestRelease;

    public string LatestVersionText => TargetVersion?.Version ?? "—";

    public bool IsUpdateAvailable =>
        TargetVersion is not null && CompareVersions(TargetVersion.Version, _config.Version) > 0;

    public bool IsBusy
    {
        get => _isBusy;
        private set
        {
            if (SetProperty(ref _isBusy, value))
            {
                CheckForUpdateCommand.RaiseCanExecuteChanged();
                UpdateCommand.RaiseCanExecuteChanged();
                UpdateFromZipCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public string VersionStatus
    {
        get => _versionStatus;
        private set => SetProperty(ref _versionStatus, value);
    }

    public string ProgressMessage
    {
        get => _progressMessage;
        private set => SetProperty(ref _progressMessage, value);
    }

    public AsyncRelayCommand CheckForUpdateCommand { get; }
    public AsyncRelayCommand UpdateCommand { get; }
    public AsyncRelayCommand UpdateFromZipCommand { get; }

    private async Task CheckForUpdateAsync()
    {
        IsBusy = true;
        VersionStatus = "公式サイトの最新版を確認中...";
        try
        {
            var versions = await _bedrockServer.GetAvailableVersionsAsync();
            _latestRelease = versions.FirstOrDefault(v => !v.IsPreview);
            _latestPreview = versions.FirstOrDefault(v => v.IsPreview);
            RefreshVersionState();
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or InvalidOperationException)
        {
            VersionStatus = $"最新版を取得できませんでした: {ex.Message}\n公式サイトから zip を入手して「zip から更新」も使えます。";
        }
        finally
        {
            IsBusy = false;
        }
    }

    private Task UpdateFromOfficialAsync()
    {
        var target = TargetVersion;
        if (target is null)
            return Task.CompletedTask;

        return RunUpdateAsync(
            target.Version,
            progress => _bedrockServer.InstallFromUrlAsync(target.DownloadUrl, _config.DirectoryPath, progress));
    }

    private Task UpdateFromZipAsync()
    {
        var dialog = new OpenFileDialog
        {
            Filter = "統合版サーバー (bedrock-server-*.zip)|*.zip",
            CheckFileExists = true
        };
        if (dialog.ShowDialog() != true)
            return Task.CompletedTask;

        var zipPath = dialog.FileName;
        var version = BedrockServerService.TryGetVersionFromFileName(zipPath) ?? _config.Version;
        return RunUpdateAsync(
            version,
            progress => _bedrockServer.InstallFromZipAsync(zipPath, _config.DirectoryPath, progress));
    }

    private async Task RunUpdateAsync(string version, Func<IProgress<string>, Task> install)
    {
        if (_getStatus() != ServerStatus.Stopped)
        {
            _dialog.Show("サーバーを停止してから更新してください。", "確認", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        if (_dialog.Show(
                $"統合版サーバーを {version} に更新します。\n更新前に現在のワールドをバックアップします。設定・ワールド・参加許可リストはそのまま残ります。",
                "確認", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
        {
            return;
        }

        IsBusy = true;
        var progress = new Progress<string>(message => ProgressMessage = message);
        try
        {
            BackupCurrentWorld(progress);
            await install(progress);
            _config.Version = version;
            _configs.Save(_config);
            OnPropertyChanged(nameof(InstalledVersion));
            RefreshVersionState();
            VersionStatus = $"✓ {version} に更新しました。";
            Updated?.Invoke();
        }
        catch (Exception ex)
        {
            _dialog.Show($"更新に失敗しました: {ex.Message}", "エラー", MessageBoxButton.OK, MessageBoxImage.Error);
            VersionStatus = "更新に失敗しました。";
        }
        finally
        {
            ProgressMessage = string.Empty;
            IsBusy = false;
        }
    }

    private void BackupCurrentWorld(IProgress<string> progress)
    {
        var worldPath = Path.Combine(_worlds.GetWorldsRoot(_config.DirectoryPath), _config.WorldName);
        if (!Directory.Exists(worldPath))
            return;
        progress.Report("更新前のバックアップを作成中...");
        _worlds.CreateWorldBackup(_config.DirectoryPath, _config.WorldName);
    }

    private void RefreshVersionState()
    {
        OnPropertyChanged(nameof(TargetVersion));
        OnPropertyChanged(nameof(LatestVersionText));
        OnPropertyChanged(nameof(IsUpdateAvailable));
        UpdateCommand.RaiseCanExecuteChanged();
        if (TargetVersion is null)
            return;
        VersionStatus = IsUpdateAvailable
            ? $"新しいバージョン {TargetVersion.Version} があります。"
            : "最新版を使用中です。";
    }

    /// <summary>
    /// "1.21.100.7" 形式のバージョン比較。left が解釈できなければ 0（更新なし扱い）、
    /// right（インストール済み）だけ解釈できなければ left を新しいとみなす。
    /// </summary>
    public static int CompareVersions(string? left, string? right)
    {
        if (!Version.TryParse(left, out var l))
            return 0;
        return Version.TryParse(right, out var r) ? l.CompareTo(r) : 1;
    }
}
