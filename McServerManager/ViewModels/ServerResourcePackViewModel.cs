using System.IO;
using System.Windows;
using McServerManager.Models;
using McServerManager.Services;
using McServerManager.Utilities;
using OpenFileDialog = Microsoft.Win32.OpenFileDialog;

namespace McServerManager.ViewModels;

public sealed class ServerResourcePackViewModel : ObservableObject, IDisposable
{
    private readonly AppServices _services;
    private readonly ServerConfig _config;
    private readonly IResourcePackService _resourcePackService;

    private string _resourcePackPath = string.Empty;
    private int _httpPort = 8000;
    private bool _isServerRunning;
    private string _publicIp = "-";
    private string _sha1Hash = string.Empty;
    private string _statusMessage = string.Empty;
    private bool _isBusy;

    public ServerResourcePackViewModel(AppServices services, ServerConfig config)
    {
        _services = services;
        _config = config;
        _resourcePackService = new ResourcePackService();

        BrowseFileCommand = new RelayCommand(_ => BrowseFile());
        StartServerCommand = new AsyncRelayCommand(StartServerAsync, () => !_isBusy && !_isServerRunning && File.Exists(_resourcePackPath));
        StopServerCommand = new AsyncRelayCommand(StopServerAsync, () => !_isBusy && _isServerRunning);
        RefreshPublicIpCommand = new AsyncRelayCommand(RefreshPublicIpAsync);
        CopyUrlCommand = new RelayCommand(_ => CopyToClipboard(ResourcePackUrl), _ => !string.IsNullOrEmpty(ResourcePackUrl));
        CopySha1Command = new RelayCommand(_ => CopyToClipboard(_sha1Hash), _ => !string.IsNullOrEmpty(_sha1Hash));
        ApplyToPropertiesCommand = new AsyncRelayCommand(ApplyToPropertiesAsync, () => !string.IsNullOrEmpty(ResourcePackUrl));
        OpenPortCommand = new AsyncRelayCommand(OpenPortAsync);
        ClosePortCommand = new AsyncRelayCommand(ClosePortAsync);
    }

    public string ResourcePackPath
    {
        get => _resourcePackPath;
        private set
        {
            if (SetProperty(ref _resourcePackPath, value))
            {
                OnPropertyChanged(nameof(ResourcePackFileName));
                OnPropertyChanged(nameof(ResourcePackUrl));
                RefreshSha1();
                RefreshCommandStates();
            }
        }
    }

    public string ResourcePackFileName =>
        string.IsNullOrEmpty(_resourcePackPath) ? "（未選択）" : Path.GetFileName(_resourcePackPath);

    public int HttpPort
    {
        get => _httpPort;
        set
        {
            if (SetProperty(ref _httpPort, value))
                OnPropertyChanged(nameof(ResourcePackUrl));
        }
    }

    public bool IsServerRunning
    {
        get => _isServerRunning;
        private set
        {
            if (SetProperty(ref _isServerRunning, value))
                RefreshCommandStates();
        }
    }

    public string PublicIp
    {
        get => _publicIp;
        private set
        {
            if (SetProperty(ref _publicIp, value))
                OnPropertyChanged(nameof(ResourcePackUrl));
        }
    }

    public string ResourcePackUrl =>
        _publicIp == "-" || string.IsNullOrEmpty(_resourcePackPath)
            ? string.Empty
            : $"http://{_publicIp}:{_httpPort}/{Path.GetFileName(_resourcePackPath)}";

    public string Sha1Hash
    {
        get => _sha1Hash;
        private set => SetProperty(ref _sha1Hash, value);
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
                RefreshCommandStates();
        }
    }

    public AsyncRelayCommand StartServerCommand { get; }
    public AsyncRelayCommand StopServerCommand { get; }
    public AsyncRelayCommand RefreshPublicIpCommand { get; }
    public AsyncRelayCommand ApplyToPropertiesCommand { get; }
    public AsyncRelayCommand OpenPortCommand { get; }
    public AsyncRelayCommand ClosePortCommand { get; }
    public RelayCommand BrowseFileCommand { get; }
    public RelayCommand CopyUrlCommand { get; }
    public RelayCommand CopySha1Command { get; }

    private void BrowseFile()
    {
        var dialog = new OpenFileDialog
        {
            Title = "リソースパック ZIP を選択",
            Filter = "ZIP ファイル|*.zip|すべてのファイル|*.*",
            CheckFileExists = true
        };
        if (dialog.ShowDialog() != true)
            return;

        ResourcePackPath = dialog.FileName;
        StatusMessage = string.Empty;
    }

    private async Task StartServerAsync()
    {
        IsBusy = true;
        StatusMessage = "HTTPサーバーを起動中...";
        try
        {
            await _resourcePackService.StartAsync(_resourcePackPath, _httpPort);
            IsServerRunning = true;
            StatusMessage = $"ポート {_httpPort} で配信中";
        }
        catch (Exception ex)
        {
            StatusMessage = $"起動失敗: {ex.Message}";
            _services.Dialog.Show(
                $"HTTPサーバーの起動に失敗しました。\n管理者権限で起動するか、以下のコマンドを実行してください:\n\nnetsh http add urlacl url=http://+:{_httpPort}/ user=Everyone\n\n詳細: {ex.Message}",
                "エラー", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task StopServerAsync()
    {
        IsBusy = true;
        StatusMessage = "HTTPサーバーを停止中...";
        try
        {
            await _resourcePackService.StopAsync();
            IsServerRunning = false;
            StatusMessage = "停止しました";
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task RefreshPublicIpAsync()
    {
        StatusMessage = "グローバルIPを取得中...";
        var ip = await _services.Network.GetPublicIpAsync();
        PublicIp = string.IsNullOrWhiteSpace(ip) ? "-" : ip;
        StatusMessage = string.IsNullOrWhiteSpace(ip) ? "IP取得失敗" : string.Empty;
        RefreshCommandStates();
    }

    private async Task ApplyToPropertiesAsync()
    {
        try
        {
            var props = _services.Properties.Load(_config.DirectoryPath, _config);
            props.ResourcePack = ResourcePackUrl;
            props.ResourcePackSha1 = _sha1Hash;
            _services.Properties.Save(_config.DirectoryPath, props);
            _services.Dialog.Show(
                $"server.properties にリソースパック設定を書き込みました。\n\nresource-pack={ResourcePackUrl}\nresource-pack-sha1={_sha1Hash}",
                "完了", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            _services.Dialog.Show(
                $"server.properties への書き込みに失敗しました: {ex.Message}",
                "エラー", MessageBoxButton.OK, MessageBoxImage.Error);
        }

        await Task.CompletedTask;
    }

    private async Task OpenPortAsync()
    {
        var (ok, error) = await _services.Upnp.TryOpenPortAsync(_httpPort, $"MaiPilot_ResourcePack_{_config.Name}");
        if (!ok)
        {
            _services.Dialog.Show(
                error ?? "ポート開放に失敗しました。",
                "警告", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        _services.Dialog.Show($"TCP {_httpPort} のポート開放を実行しました。", "完了", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private async Task ClosePortAsync()
    {
        await _services.Upnp.TryClosePortAsync(_httpPort);
        _services.Dialog.Show($"TCP {_httpPort} のポート閉鎖を実行しました。", "完了", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private void RefreshSha1()
    {
        if (!File.Exists(_resourcePackPath))
        {
            Sha1Hash = string.Empty;
            return;
        }
        try
        {
            Sha1Hash = _resourcePackService.ComputeSha1(_resourcePackPath);
        }
        catch
        {
            Sha1Hash = string.Empty;
        }
    }

    private void RefreshCommandStates()
    {
        StartServerCommand.RaiseCanExecuteChanged();
        StopServerCommand.RaiseCanExecuteChanged();
        CopyUrlCommand.RaiseCanExecuteChanged();
        CopySha1Command.RaiseCanExecuteChanged();
        ApplyToPropertiesCommand.RaiseCanExecuteChanged();
    }

    private static void CopyToClipboard(string text)
    {
        try { Clipboard.SetText(text); }
        catch { }
    }

    public void Dispose()
    {
        if (_isServerRunning)
            _ = _resourcePackService.StopAsync();
    }
}
