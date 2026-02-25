using System.Collections.Specialized;
using System.Windows.Controls;
using System.Windows;
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
}
