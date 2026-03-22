using System.Collections.Specialized;
using System.Windows.Controls;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Media3D;
using McServerManager.ViewModels;

namespace McServerManager.Views;

public partial class ServerDetailControl : System.Windows.Controls.UserControl
{
    public ServerDetailControl()
    {
        InitializeComponent();
        Loaded += OnLoaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (LogListBox.ItemsSource is INotifyCollectionChanged collection)
        {
            collection.CollectionChanged += OnLogsCollectionChanged;
        }
    }

    private void OnLogsCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (LogListBox.Items.Count > 0)
        {
            var lastItem = LogListBox.Items[^1];
            LogListBox.ScrollIntoView(lastItem);
        }
    }

    private void AddonDropZone_OnDragOver(object sender, System.Windows.DragEventArgs e)
    {
        if (e.Data.GetDataPresent(System.Windows.DataFormats.FileDrop))
        {
            e.Effects = System.Windows.DragDropEffects.Copy;
        }
        else
        {
            e.Effects = System.Windows.DragDropEffects.None;
        }

        e.Handled = true;
    }

    private void AddonDropZone_OnDrop(object sender, System.Windows.DragEventArgs e)
    {
        if (DataContext is not ServerViewModel vm)
        {
            return;
        }

        if (!e.Data.GetDataPresent(System.Windows.DataFormats.FileDrop))
        {
            return;
        }

        if (e.Data.GetData(System.Windows.DataFormats.FileDrop) is string[] paths && paths.Length > 0)
        {
            _ = vm.ImportAddonsAsync(paths);
        }

        e.Handled = true;
    }

    private void SettingsScrollViewer_OnPreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (sender is not ScrollViewer settingsScrollViewer || e.OriginalSource is not DependencyObject source)
        {
            return;
        }

        var nestedScrollViewer = FindAncestor<ScrollViewer>(source);
        if (nestedScrollViewer is not null
            && nestedScrollViewer != settingsScrollViewer
            && CanScrollInDirection(nestedScrollViewer, e.Delta))
        {
            return;
        }

        var nextOffset = Math.Clamp(
            settingsScrollViewer.VerticalOffset - (e.Delta / 3.0),
            0,
            settingsScrollViewer.ScrollableHeight);

        settingsScrollViewer.ScrollToVerticalOffset(nextOffset);
        e.Handled = true;
    }

    private static bool CanScrollInDirection(ScrollViewer scrollViewer, int delta)
    {
        return delta > 0
            ? scrollViewer.VerticalOffset > 0
            : delta < 0 && scrollViewer.VerticalOffset < scrollViewer.ScrollableHeight;
    }

    private static T? FindAncestor<T>(DependencyObject? start) where T : DependencyObject
    {
        var current = start;
        while (current is not null)
        {
            if (current is T typed)
            {
                return typed;
            }

            current = GetParentObject(current);
        }

        return null;
    }

    private static DependencyObject? GetParentObject(DependencyObject current)
    {
        if (current is FrameworkContentElement frameworkContentElement)
        {
            return frameworkContentElement.Parent;
        }

        if (current is ContentElement contentElement)
        {
            return ContentOperations.GetParent(contentElement);
        }

        try
        {
            if (current is Visual || current is Visual3D)
            {
                return VisualTreeHelper.GetParent(current);
            }
        }
        catch (InvalidOperationException)
        {
            // Fallback to logical tree for non-visual original sources such as Run.
        }

        return LogicalTreeHelper.GetParent(current);
    }
}
