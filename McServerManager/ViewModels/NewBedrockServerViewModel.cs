using System.Collections.ObjectModel;
using System.Net.Http;
using McServerManager.Models;
using McServerManager.Services;
using McServerManager.Utilities;
using OpenFileDialog = Microsoft.Win32.OpenFileDialog;

namespace McServerManager.ViewModels;

/// <summary>新規作成ウィザードの「統合版 (BE)」フォーム。</summary>
public sealed class NewBedrockServerViewModel : ObservableObject
{
    public const string SourceRelease = "release";
    public const string SourcePreview = "preview";
    public const string SourceZip = "zip";

    private readonly IBedrockServerService _bedrockServer;
    private BedrockVersionInfo? _latestRelease;
    private BedrockVersionInfo? _latestPreview;
    private BedrockPreset? _selectedPreset;
    private string _source = SourceRelease;
    private string _zipPath = string.Empty;
    private string _gameMode = "survival";
    private string _difficulty = "normal";
    private bool _allowCheats;
    private int _maxPlayers = 10;
    private int _port = ServerEditions.BedrockDefaultPort;
    private int _portV6 = ServerEditions.BedrockDefaultPortV6;
    private bool _onlineMode = true;
    private bool _useRakNet = true;
    private bool _isLoadingVersions;
    private string _versionStatus = string.Empty;
    private Task? _loadTask;

    public NewBedrockServerViewModel(IBedrockServerService bedrockServer)
    {
        _bedrockServer = bedrockServer;
        Presets = new ObservableCollection<BedrockPreset>
        {
            new("friends", "★", "友達とサバイバル", "スマホ・Switch の友達と。定番の設定。", "survival", "normal", false, 10),
            new("creative", "▣", "クリエイティブ建築", "資源無限で建築。コマンドも使える。", "creative", "peaceful", true, 10),
            new("family", "♥", "家族・キッズ向け", "敵が出ないおだやかな世界。", "survival", "peaceful", false, 6),
            new("hard", "▲", "ガチサバイバル", "難易度ハード。上級者向け。", "survival", "hard", false, 10),
        };
        BrowseZipCommand = new RelayCommand(_ => BrowseZip());
        RetryLoadVersionsCommand = new AsyncRelayCommand(() => LoadVersionsAsync(), () => !IsLoadingVersions);
        SelectedPreset = Presets[0];
    }

    public ObservableCollection<BedrockPreset> Presets { get; }
    public IReadOnlyList<ChoiceOption> GameModes => BedrockSettingsViewModel.GameModes;
    public IReadOnlyList<ChoiceOption> Difficulties => BedrockSettingsViewModel.Difficulties;

    public BedrockPreset? SelectedPreset
    {
        get => _selectedPreset;
        set
        {
            if (SetProperty(ref _selectedPreset, value) && value is not null)
            {
                GameMode = value.GameMode;
                Difficulty = value.Difficulty;
                AllowCheats = value.AllowCheats;
                MaxPlayers = value.MaxPlayers;
            }
        }
    }

    /// <summary>"release" | "preview" | "zip"</summary>
    public string Source
    {
        get => _source;
        set
        {
            if (SetProperty(ref _source, value))
                OnPropertyChanged(nameof(SelectedVersionText));
        }
    }

    public string ZipPath
    {
        get => _zipPath;
        set
        {
            if (SetProperty(ref _zipPath, value))
                OnPropertyChanged(nameof(SelectedVersionText));
        }
    }

    public string GameMode { get => _gameMode; set => SetProperty(ref _gameMode, value); }
    public string Difficulty { get => _difficulty; set => SetProperty(ref _difficulty, value); }
    public bool AllowCheats { get => _allowCheats; set => SetProperty(ref _allowCheats, value); }
    public int MaxPlayers { get => _maxPlayers; set => SetProperty(ref _maxPlayers, value); }
    public int Port { get => _port; set => SetProperty(ref _port, value); }
    public int PortV6 { get => _portV6; set => SetProperty(ref _portV6, value); }
    public bool OnlineMode { get => _onlineMode; set => SetProperty(ref _onlineMode, value); }

    /// <summary>
    /// 通信方式。NetherNet (BDS の既定) は「一覧に見えるのに入れない」不具合報告 (BDS-23108) があり、
    /// RakNet なら接続できたという報告が多いため、作成時の既定は RakNet にする。
    /// </summary>
    public bool UseRakNet
    {
        get => _useRakNet;
        set
        {
            if (SetProperty(ref _useRakNet, value))
                OnPropertyChanged(nameof(UseNetherNet));
        }
    }

    public bool UseNetherNet
    {
        get => !_useRakNet;
        set => UseRakNet = !value;
    }

    public bool IsLoadingVersions
    {
        get => _isLoadingVersions;
        private set
        {
            if (SetProperty(ref _isLoadingVersions, value))
                RetryLoadVersionsCommand.RaiseCanExecuteChanged();
        }
    }

    public string VersionStatus
    {
        get => _versionStatus;
        private set => SetProperty(ref _versionStatus, value);
    }

    public string LatestReleaseText => _latestRelease?.Version ?? (IsLoadingVersions ? "確認中..." : "—");
    public string LatestPreviewText => _latestPreview?.Version ?? (IsLoadingVersions ? "確認中..." : "—");

    /// <summary>作成されるバージョンの要約（フッター・確認用）。</summary>
    public string SelectedVersionText => Source switch
    {
        SourceZip => string.IsNullOrWhiteSpace(ZipPath)
            ? "zip 未選択"
            : $"zip: {BedrockServerService.TryGetVersionFromFileName(ZipPath) ?? Path.GetFileName(ZipPath)}",
        SourcePreview => $"プレビュー {LatestPreviewText}",
        _ => $"正式版 {LatestReleaseText}",
    };

    public RelayCommand BrowseZipCommand { get; }
    public AsyncRelayCommand RetryLoadVersionsCommand { get; }

    /// <summary>初回表示時に一度だけ最新版を取得する。</summary>
    public Task EnsureVersionsLoadedAsync() => _loadTask ??= LoadVersionsAsync();

    public async Task LoadVersionsAsync()
    {
        IsLoadingVersions = true;
        VersionStatus = "公式サイトから最新版を確認中...";
        RaiseVersionTexts();
        try
        {
            var versions = await _bedrockServer.GetAvailableVersionsAsync();
            _latestRelease = versions.FirstOrDefault(v => !v.IsPreview);
            _latestPreview = versions.FirstOrDefault(v => v.IsPreview);
            VersionStatus = string.Empty;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or InvalidOperationException)
        {
            VersionStatus = "最新版を取得できませんでした。ネットワークを確認して再試行するか、公式サイトで入手した zip を指定してください。";
            if (_latestRelease is null && _latestPreview is null)
                Source = SourceZip;
        }
        finally
        {
            IsLoadingVersions = false;
            RaiseVersionTexts();
        }
    }

    /// <summary>入力チェック。問題があればユーザー向けメッセージを返す。</summary>
    public string? Validate()
    {
        if (Source == SourceZip)
        {
            if (string.IsNullOrWhiteSpace(ZipPath) || !File.Exists(ZipPath))
                return "統合版サーバーの zip を選択してください。";
        }
        else if (ResolveOfficialVersion() is null)
        {
            return IsLoadingVersions
                ? "最新版を確認中です。少し待ってから作成してください。"
                : "最新版を取得できませんでした。「再試行」するか zip を指定してください。";
        }

        if (Port is < 1 or > 65535 || PortV6 is < 1 or > 65535)
            return "ポート番号は 1〜65535 で入力してください。";
        if (Port == PortV6)
            return "IPv4 と IPv6 のポートは別の番号にしてください。";
        if (MaxPlayers is < 1 or > 200)
            return "最大人数は 1〜200 で入力してください。";
        return null;
    }

    public void ApplyTo(NewServerOptions options)
    {
        options.Type = ServerEditions.BedrockType;
        options.GameMode = GameMode;
        options.Difficulty = Difficulty;
        options.AllowCheats = AllowCheats;
        options.MaxPlayers = MaxPlayers;
        options.Port = Port;
        options.PortV6 = PortV6;
        options.OnlineMode = OnlineMode;
        options.BedrockTransport = UseRakNet ? BedrockNetworkPlanner.TransportRakNet : BedrockNetworkPlanner.TransportNetherNet;

        if (Source == SourceZip)
        {
            options.BedrockZipPath = ZipPath;
            options.Version = BedrockServerService.TryGetVersionFromFileName(ZipPath) ?? "unknown";
            return;
        }

        var version = ResolveOfficialVersion()!;
        options.BedrockDownloadUrl = version.DownloadUrl;
        options.Version = version.Version;
    }

    private BedrockVersionInfo? ResolveOfficialVersion() =>
        Source == SourcePreview ? _latestPreview : _latestRelease;

    private void BrowseZip()
    {
        var dialog = new OpenFileDialog
        {
            Filter = "統合版サーバー (bedrock-server-*.zip)|*.zip",
            CheckFileExists = true
        };
        if (dialog.ShowDialog() == true)
        {
            ZipPath = dialog.FileName;
            Source = SourceZip;
        }
    }

    private void RaiseVersionTexts()
    {
        OnPropertyChanged(nameof(LatestReleaseText));
        OnPropertyChanged(nameof(LatestPreviewText));
        OnPropertyChanged(nameof(SelectedVersionText));
    }
}

/// <summary>統合版おまかせ作成のプリセット。</summary>
public sealed record BedrockPreset(
    string Id,
    string Glyph,
    string Title,
    string Subtitle,
    string GameMode,
    string Difficulty,
    bool AllowCheats,
    int MaxPlayers)
{
    /// <summary>カード下部の要約（例: "サバイバル · ノーマル · 10人"）。</summary>
    public string Summary =>
        $"{LabelOf(BedrockSettingsViewModel.GameModes, GameMode)} · {LabelOf(BedrockSettingsViewModel.Difficulties, Difficulty)} · {MaxPlayers}人";

    private static string LabelOf(IReadOnlyList<ChoiceOption> options, string id) =>
        options.FirstOrDefault(o => o.Id == id)?.Label ?? id;
}
