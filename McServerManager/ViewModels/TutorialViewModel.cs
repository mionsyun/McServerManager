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
    private readonly AppSettingsService _settingsService;
    private readonly List<TutorialStep> _steps;
    private int _stepIndex;
    private bool _isActive;
    private WpfRect _highlightRect = WpfRect.Empty;
    private WpfPoint _cardPosition = new(80, 80);

    public TutorialViewModel(AppSettings settings, AppSettingsService settingsService)
    {
        _settings = settings;
        _settingsService = settingsService;

        _steps = new List<TutorialStep>
        {
            new("ようこそ",
                "このガイドでは、Minecraft サーバーを作るまでの流れを一緒に進めます。",
                null,
                TutorialPlacement.Center),
            new("Java を確認",
                "Java が見つからない場合は「設定」タブで java.exe を指定できます。",
                "JavaPathTextBox",
                TutorialPlacement.Right,
                "SettingsTab"),
            new("サーバー作成",
                "まずはサーバーを新規作成します。名前と保存先を決めましょう。",
                "NewServerButton",
                TutorialPlacement.Right),
            new("初期設定",
                "ポートや最大人数など、基本設定を整えます。",
                "SettingsTab",
                TutorialPlacement.Bottom,
                "SettingsTab"),
            new("サーバー起動",
                "起動ボタンでサーバーを立ち上げます。",
                "StartServerButton",
                TutorialPlacement.Left),
            new("接続確認",
                "友達に共有するアドレスを確認します。",
                "ShareAddressText",
                TutorialPlacement.Top,
                "NetworkTab"),
            new("Windows Firewall",
                "外部公開する場合は Windows Firewall の受信ルールを作成します。",
                "FirewallSection",
                TutorialPlacement.Right,
                "NetworkTab"),
            new("完了",
                "準備は完了です。いつでも「セットアップガイド」から見直せます。",
                null,
                TutorialPlacement.Center)
        };

        NextCommand = new RelayCommand(_ => Next());
        BackCommand = new RelayCommand(_ => Back(), _ => CanGoBack);
        SkipCommand = new RelayCommand(_ => Finish());
        FinishCommand = new RelayCommand(_ => Finish());
    }

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

    public void Start()
    {
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
