using System.Windows;
using WpfPoint = System.Windows.Point;
using WpfRect = System.Windows.Rect;
using McServerManager.Models;
using McServerManager.Services;
using McServerManager.Utilities;

namespace McServerManager.ViewModels;

public sealed class TutorialViewModel : ObservableObject
{
    private readonly AppSettings _settings;
    private readonly IAppSettingsService _settingsService;
    private readonly List<TutorialStep> _steps;
    private int _stepIndex;
    private bool _isActive;
    private WpfRect _highlightRect = WpfRect.Empty;
    private WpfPoint _cardPosition = new(80, 80);

    public TutorialViewModel(AppSettings settings, IAppSettingsService settingsService)
    {
        _settings = settings;
        _settingsService = settingsService;

        _steps = BuildSteps(GuideType.InitialSetup);

        NextCommand = new RelayCommand(_ => Next());
        BackCommand = new RelayCommand(_ => Back(), _ => CanGoBack);
        SkipCommand = new RelayCommand(_ => Finish());
        FinishCommand = new RelayCommand(_ => Finish());
    }

    private static List<TutorialStep> BuildSteps(GuideType type) => type switch
    {
        GuideType.InitialSetup => BuildInitialSetupSteps(),
        GuideType.DistributionMap => BuildDistributionMapSteps(),
        GuideType.Mod => BuildModSteps(),
        GuideType.Plugin => BuildPluginSteps(),
        GuideType.ResourcePack => BuildResourcePackSteps(),
        _ => BuildInitialSetupSteps()
    };

    private static List<TutorialStep> BuildInitialSetupSteps() =>
    [
        new("ようこそ MaiPilot へ",
            "このガイドでは、Minecraft サーバーを立ち上げて友達と遊べるようにするまでの全手順を案内します。画面上の要素をハイライトしながら進めるので、迷わず操作できます。",
            null,
            TutorialPlacement.Center),
        new("Java の確認",
            "Minecraft サーバーには Java 17 以上が必要です。設定タブの「Java パス」欄に java.exe のパスを入力してください。Java がない場合は Adoptium (Eclipse Temurin) から無料でダウンロードできます。",
            "JavaPathTextBox",
            TutorialPlacement.Right,
            "SettingsTab"),
        new("サーバーを新規作成",
            "「＋ サーバーを追加」ボタンでサーバー作成ダイアログを開きます。サーバー名・保存フォルダ・種類（Vanilla / Paper など）・バージョンを選んで作成してください。",
            "NewServerButton",
            TutorialPlacement.Right),
        new("基本設定を確認",
            "設定タブでポート番号（既定: 25565）・割り当てメモリ・最大プレイヤー数を調整できます。変更した場合は「保存」を押してください。",
            "SettingsTab",
            TutorialPlacement.Bottom,
            "SettingsTab"),
        new("サーバーを起動",
            "「起動」ボタンでサーバーを立ち上げます。初回は EULA への同意処理が自動で行われます。起動には数十秒かかる場合があります。",
            "StartServerButton",
            TutorialPlacement.Left),
        new("コンソールを確認",
            "コンソールタブでサーバーのログをリアルタイムに確認できます。「Done! For help, type 'help'」と表示されれば起動完了です。コマンド欄からサーバーコマンドを直接実行できます。",
            "LogListBox",
            TutorialPlacement.Top),
        new("LAN で接続する",
            "同じ Wi-Fi / LAN 内のプレイヤーは「LAN IP」アドレス:ポート番号（例: 192.168.1.10:25565）で接続できます。ネットワークタブで LAN IP を確認してください。",
            "NetworkTab",
            TutorialPlacement.Bottom,
            "NetworkTab"),
        new("外部公開（ポート開放）",
            "インターネット越しに接続するにはルーターで TCP/UDP ポートの開放が必要です。UPnP 対応ルーターなら「開放」ボタンで自動設定できます。非対応の場合はルーター管理画面で手動設定してください。",
            "FirewallSection",
            TutorialPlacement.Right,
            "NetworkTab"),
        new("共有アドレスを確認",
            "「取得」ボタンでグローバル IP を確認し、友達に共有してください。友達は「IP:ポート番号」（例: 203.0.113.1:25565）で Minecraft から直接接続できます。",
            "ShareAddressText",
            TutorialPlacement.Top,
            "NetworkTab"),
        new("セットアップ完了！",
            "これで Minecraft サーバーの準備は完了です。問題が発生したときはコンソールのエラーログを確認してください。いつでも「セットアップガイド」ボタンから見直せます。",
            null,
            TutorialPlacement.Center)
    ];

    private static List<TutorialStep> BuildDistributionMapSteps() =>
    [
        new("配布マップとは",
            "配布マップは他のプレイヤーが作成・公開しているワールドデータです。マップデータをサーバーに設定することで、そのマップでプレイできます。Planet Minecraft や CurseForge から入手できます。",
            null,
            TutorialPlacement.Center),
        new("マップデータを入手",
            "Planet Minecraft や CurseForge などのサイトから配布マップ（.zip またはフォルダ）をダウンロードしてください。ダウンロード後は ZIP を解凍しておきます。",
            null,
            TutorialPlacement.Center),
        new("ワールドフォルダに配置",
            "「ワールド」タブの「フォルダを開く」でサーバーフォルダをエクスプローラーで開き、解凍したマップデータをサーバーフォルダ内に配置してください。",
            "WorldTab",
            TutorialPlacement.Bottom,
            "WorldTab"),
        new("ワールドを切り替え",
            "「ワールド」タブのドロップダウンからマップ名を選択し「切り替え」を押すと、server.properties の level-name が自動で更新されます。",
            "WorldTab",
            TutorialPlacement.Bottom,
            "WorldTab"),
        new("サーバーを再起動",
            "ワールド切り替え後はサーバーを再起動してください。再起動することで新しいマップが読み込まれます。",
            "StartServerButton",
            TutorialPlacement.Left),
        new("動作確認",
            "コンソールに「Done!」と表示されれば起動完了です。接続して配布マップが正しく読み込まれているか確認してください。",
            "LogListBox",
            TutorialPlacement.Top),
        new("配布マップ導入完了！",
            "配布マップの設定は完了です。マップによっては専用のゲームモードやルールがあります。マップの説明ファイルも確認しましょう。",
            null,
            TutorialPlacement.Center)
    ];

    private static List<TutorialStep> BuildModSteps() =>
    [
        new("MOD とは",
            "MOD はゲームの機能を拡張する追加プログラムです。新しいアイテム・生物・システムを追加できます。MOD 対応サーバー（Forge / Fabric）が必要で、クライアント側にも同じ MOD が必要です。",
            null,
            TutorialPlacement.Center),
        new("MOD 対応サーバーを確認",
            "設定タブで「サーバー種類」が Forge または Fabric であることを確認してください。Vanilla サーバーには MOD を追加できません。",
            "SettingsTab",
            TutorialPlacement.Bottom,
            "SettingsTab"),
        new("MOD を入手",
            "Modrinth や CurseForge から .jar 形式の MOD ファイルをダウンロードします。サーバーのバージョン・ローダー（Forge/Fabric）と一致するものを選んでください。",
            null,
            TutorialPlacement.Center),
        new("MOD を追加",
            "「MOD/プラグイン」タブで「追加」ボタンを押してファイルを選択するか、Modrinth 検索から直接インストールできます。",
            "AddonTab",
            TutorialPlacement.Bottom,
            "AddonTab"),
        new("クライアント側も設定",
            "MOD はサーバーとクライアント（プレイヤー側の Minecraft）の両方に同じ MOD が必要です。クライアントにも同じバージョンの MOD をインストールしてください。",
            null,
            TutorialPlacement.Center),
        new("サーバーを再起動",
            "MOD 追加後はサーバーを再起動してください。コンソールで MOD の読み込みログを確認できます。",
            "StartServerButton",
            TutorialPlacement.Left),
        new("動作確認",
            "コンソールで各 MOD の読み込みログを確認してください。エラーが出た場合はバージョン互換性を見直してください。",
            "LogListBox",
            TutorialPlacement.Top),
        new("MOD 導入完了！",
            "MOD の導入は完了です。MOD 同士の競合が発生する場合は、1つずつ追加しながら原因を特定してください。",
            null,
            TutorialPlacement.Center)
    ];

    private static List<TutorialStep> BuildPluginSteps() =>
    [
        new("プラグインとは",
            "プラグインはサーバーに機能を追加するプログラムです。MOD と違いクライアント側の変更は不要です。チャット管理・経済システム・ミニゲームなど多様な機能を追加できます。",
            null,
            TutorialPlacement.Center),
        new("プラグイン対応サーバーを確認",
            "設定タブで「サーバー種類」が Paper または Spigot であることを確認してください。Vanilla / Forge / Fabric サーバーにはプラグインを追加できません。",
            "SettingsTab",
            TutorialPlacement.Bottom,
            "SettingsTab"),
        new("プラグインを入手",
            "Modrinth や SpigotMC、Hangar から .jar 形式のプラグインファイルをダウンロードします。サーバーのバージョンに対応したものを選んでください。",
            null,
            TutorialPlacement.Center),
        new("プラグインを追加",
            "「MOD/プラグイン」タブで「追加」ボタンを押してファイルを選択するか、Modrinth 検索から直接インストールできます。",
            "AddonTab",
            TutorialPlacement.Bottom,
            "AddonTab"),
        new("サーバーを再起動",
            "プラグイン追加後はサーバーを再起動してください。初回起動時にプラグインの設定ファイルが plugins/<プラグイン名>/ フォルダに生成されます。",
            "StartServerButton",
            TutorialPlacement.Left),
        new("設定ファイルを確認",
            "コンソールタブでプラグインの読み込みログを確認してください。設定ファイルを編集後、多くのプラグインは /reload または再起動で変更が反映されます。",
            "LogListBox",
            TutorialPlacement.Top),
        new("プラグイン導入完了！",
            "プラグインの導入は完了です。プラグイン同士の競合が発生した場合は、1つずつ追加しながら原因を特定してください。",
            null,
            TutorialPlacement.Center)
    ];

    private static List<TutorialStep> BuildResourcePackSteps() =>
    [
        new("リソースパックとは",
            "リソースパックはテクスチャ・サウンド・フォントなどのビジュアルをカスタマイズする ZIP ファイルです。サーバーに設定するとプレイヤーが接続時に自動ダウンロードできます。",
            null,
            TutorialPlacement.Center),
        new("リソースパックを入手",
            "Planet Minecraft や CurseForge などから .zip 形式のリソースパックをダウンロードしてください。解凍せず ZIP ファイルのまま使います。",
            null,
            TutorialPlacement.Center),
        new("ファイルを選択",
            "「リソースパック」タブで「参照...」ボタンを押し、ダウンロードした ZIP ファイルを選択してください。SHA-1 ハッシュが自動で計算されます。",
            "ResourcePackTab",
            TutorialPlacement.Bottom,
            "ResourcePackTab"),
        new("HTTPS 配信を開始",
            "「Cloudflare トンネルで配信開始」ボタンを押すと HTTPS の配信 URL が発行されます。Minecraft は HTTPS がほぼ必須のため、このトンネル機能を使うことを推奨します（初回のみ約 25MB ダウンロード）。",
            "ResourcePackTab",
            TutorialPlacement.Bottom,
            "ResourcePackTab"),
        new("server.properties に書き込む",
            "配信 URL が表示されたら「server.properties に書き込む」ボタンを押して設定を保存してください。URL と SHA-1 ハッシュが自動で書き込まれます。",
            "ResourcePackTab",
            TutorialPlacement.Bottom,
            "ResourcePackTab"),
        new("サーバーを再起動して確認",
            "設定を反映するためにサーバーを再起動してください。接続したプレイヤーにリソースパックのダウンロード確認が表示されます。",
            "StartServerButton",
            TutorialPlacement.Left),
        new("リソースパック設定完了！",
            "リソースパックの設定は完了です。配信を止めるときは「停止」ボタンを押してください。URL が変わった場合は再度 server.properties に書き込んでください。",
            null,
            TutorialPlacement.Center)
    ];

    public event EventHandler? StepChanged;

    public RelayCommand NextCommand { get; }
    public RelayCommand BackCommand { get; }
    public RelayCommand SkipCommand { get; }
    public RelayCommand FinishCommand { get; }

    public bool IsActive
    {
        get => _isActive;
        private set
        {
            if (SetProperty(ref _isActive, value))
            {
                OnPropertyChanged(nameof(IsInactive));
                StepChanged?.Invoke(this, EventArgs.Empty);
            }
        }
    }

    public bool IsInactive => !IsActive;

    public int TotalSteps => _steps.Count;

    public int StepIndex
    {
        get => _stepIndex;
        private set
        {
            if (SetProperty(ref _stepIndex, value))
            {
                OnPropertyChanged(nameof(CurrentStep));
                OnPropertyChanged(nameof(Title));
                OnPropertyChanged(nameof(Body));
                OnPropertyChanged(nameof(ProgressText));
                OnPropertyChanged(nameof(CanGoBack));
                OnPropertyChanged(nameof(IsLastStep));
                OnPropertyChanged(nameof(NextButtonText));
                BackCommand.RaiseCanExecuteChanged();
                StepChanged?.Invoke(this, EventArgs.Empty);
            }
        }
    }

    public TutorialStep CurrentStep => _steps[StepIndex];

    public string Title => CurrentStep.Title;
    public string Body => CurrentStep.Body;
    public string ProgressText => $"ステップ {StepIndex + 1} / {TotalSteps}";
    public bool CanGoBack => StepIndex > 0;
    public bool IsLastStep => StepIndex >= TotalSteps - 1;
    public string NextButtonText => IsLastStep ? "完了" : "次へ";

    public string? CurrentTargetName => CurrentStep.TargetName;
    public TutorialPlacement CurrentPlacement => CurrentStep.Placement;
    public string? PreferredTabName => CurrentStep.PreferredTabName;

    public WpfRect HighlightRect
    {
        get => _highlightRect;
        set
        {
            if (SetProperty(ref _highlightRect, value))
            {
                OnPropertyChanged(nameof(HasHighlight));
            }
        }
    }

    public bool HasHighlight => HighlightRect.Width > 0 && HighlightRect.Height > 0;

    public WpfPoint CardPosition
    {
        get => _cardPosition;
        set => SetProperty(ref _cardPosition, value);
    }

    public double CardWidth { get; } = 360;
    public double CardHeight { get; } = 220;
    public double HighlightCornerRadius { get; } = 10;
    public double HighlightPadding { get; } = 6;

    public void Start(GuideType type = GuideType.InitialSetup)
    {
        _steps.Clear();
        _steps.AddRange(BuildSteps(type));
        OnPropertyChanged(nameof(TotalSteps));
        StepIndex = 0;
        IsActive = true;
    }

    public void Finish()
    {
        IsActive = false;
        _settings.HasCompletedTutorial = true;
        _settingsService.Save(_settings);
    }

    private void Next()
    {
        if (IsLastStep)
        {
            Finish();
            return;
        }

        StepIndex++;
    }

    private void Back()
    {
        if (StepIndex <= 0)
        {
            return;
        }

        StepIndex--;
    }
}
