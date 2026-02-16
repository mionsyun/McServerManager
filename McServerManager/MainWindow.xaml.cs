using System.Windows;
using System.Diagnostics;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using McServerManager.ViewModels;
using WpfPoint = System.Windows.Point;
using WpfRect = System.Windows.Rect;

namespace McServerManager;

public partial class MainWindow : Window
{
    private TutorialViewModel? _tutorial;

    public MainWindow()
    {
        InitializeComponent();
        Loaded += OnLoaded;
        SizeChanged += (_, _) => UpdateTutorialLayout();
        DataContextChanged += (_, _) => BindTutorial();
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        BindTutorial();
    }

    private void BindTutorial()
    {
        if (_tutorial is not null)
        {
            _tutorial.StepChanged -= OnTutorialStepChanged;
        }

        if (DataContext is MainViewModel vm)
        {
            _tutorial = vm.Tutorial;
            _tutorial.StepChanged += OnTutorialStepChanged;
        }

        UpdateTutorialLayout();
    }

    private void OnTutorialStepChanged(object? sender, EventArgs e)
    {
        Dispatcher.BeginInvoke(new Action(UpdateTutorialLayout), System.Windows.Threading.DispatcherPriority.Loaded);
    }

    private void UpdateTutorialLayout()
    {
        if (_tutorial is null || !_tutorial.IsActive)
        {
            return;
        }

        if (TutorialOverlay is null || TutorialOverlay.ActualWidth <= 0 || TutorialOverlay.ActualHeight <= 0)
        {
            return;
        }

        if (!string.IsNullOrWhiteSpace(_tutorial.PreferredTabName))
        {
            var tab = FindElementByName<TabItem>(this, _tutorial.PreferredTabName);
            if (tab is not null && !tab.IsSelected)
            {
                tab.IsSelected = true;
            }
        }

        WpfRect highlight = WpfRect.Empty;
        if (!string.IsNullOrWhiteSpace(_tutorial.CurrentTargetName))
        {
            var target = FindElementByName<FrameworkElement>(this, _tutorial.CurrentTargetName);
            if (target is not null && target.IsVisible && target.ActualWidth > 0 && target.ActualHeight > 0)
            {
                var bounds = target.TransformToAncestor(this).TransformBounds(new Rect(0, 0, target.ActualWidth, target.ActualHeight));
                bounds.Inflate(_tutorial.HighlightPadding, _tutorial.HighlightPadding);
                highlight = bounds;
            }
        }

        _tutorial.HighlightRect = highlight;
        _tutorial.CardPosition = CalculateCardPosition(highlight);
    }

    private WpfPoint CalculateCardPosition(WpfRect highlight)
    {
        var overlayWidth = TutorialOverlay.ActualWidth;
        var overlayHeight = TutorialOverlay.ActualHeight;
        var cardWidth = _tutorial?.CardWidth ?? 360;
        var cardHeight = _tutorial?.CardHeight ?? 220;
        var margin = 20;

        if (_tutorial is null || _tutorial.CurrentPlacement == TutorialPlacement.Center || highlight.IsEmpty)
        {
            return new WpfPoint(
                Math.Max(margin, (overlayWidth - cardWidth) / 2),
                Math.Max(margin, (overlayHeight - cardHeight) / 2));
        }

        var spacing = 14;
        var x = highlight.Left;
        var y = highlight.Top;

        switch (_tutorial.CurrentPlacement)
        {
            case TutorialPlacement.Top:
                x = highlight.Left;
                y = highlight.Top - cardHeight - spacing;
                break;
            case TutorialPlacement.Bottom:
                x = highlight.Left;
                y = highlight.Bottom + spacing;
                break;
            case TutorialPlacement.Left:
                x = highlight.Left - cardWidth - spacing;
                y = highlight.Top;
                break;
            case TutorialPlacement.Right:
                x = highlight.Right + spacing;
                y = highlight.Top;
                break;
        }

        x = Math.Max(margin, Math.Min(x, overlayWidth - cardWidth - margin));
        y = Math.Max(margin, Math.Min(y, overlayHeight - cardHeight - margin));
        return new WpfPoint(x, y);
    }

    private static T? FindElementByName<T>(DependencyObject root, string name) where T : FrameworkElement
    {
        if (root is T typed && typed.Name == name)
        {
            return typed;
        }

        var count = VisualTreeHelper.GetChildrenCount(root);
        for (var i = 0; i < count; i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            var result = FindElementByName<T>(child, name);
            if (result is not null)
            {
                return result;
            }
        }

        return null;
    }

    private void OpenServerItemMenu(object sender, RoutedEventArgs e)
    {
        if (sender is System.Windows.Controls.Button button && button.ContextMenu is ContextMenu menu)
        {
            menu.PlacementTarget = button;
            menu.Placement = PlacementMode.Bottom;
            menu.IsOpen = true;
        }
    }

    private void OpenThirdPartyNotices(object sender, RoutedEventArgs e)
    {
        var path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "THIRD_PARTY_NOTICES.txt");
        if (!File.Exists(path))
        {
            System.Windows.MessageBox.Show("サードパーティ通知が見つかりません。", "エラー", MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }

        Process.Start(new ProcessStartInfo
        {
            FileName = path,
            UseShellExecute = true
        });
    }
}
