using System.Diagnostics;
using System.IO;
using McServerManager.ViewModels;
using System.Windows;
using System.Windows.Navigation;

namespace McServerManager.Views;

public partial class NewServerWindow : Window
{
    public NewServerWindow()
    {
        InitializeComponent();
    }

    private async void OnInspectTemplate(object sender, RoutedEventArgs e)
    {
        if (DataContext is not NewServerViewModel viewModel) return;
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Filter = "MaiPilot テンプレート (*.maipilot-template.json)|*.maipilot-template.json",
            CheckFileExists = true,
            Multiselect = false
        };
        if (dialog.ShowDialog(this) != true) return;
        viewModel.TemplateInspection.Cancel();
        try
        {
            await using var stream = new FileStream(dialog.FileName, FileMode.Open, FileAccess.Read,
                FileShare.Read, 4096, FileOptions.Asynchronous | FileOptions.SequentialScan);
            await viewModel.TemplateInspection.InspectAsync(stream);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            viewModel.TemplateInspection.ReportOpenFailure();
        }
    }

    private void OnCancelTemplateInspection(object sender, RoutedEventArgs e)
    {
        if (DataContext is NewServerViewModel viewModel) viewModel.TemplateInspection.Cancel();
    }

    protected override void OnClosed(EventArgs e)
    {
        if (DataContext is NewServerViewModel viewModel) viewModel.TemplateInspection.Cancel();
        base.OnClosed(e);
    }

    /// <summary>利用規約リンクを既定のブラウザで開く（https のみ）。</summary>
    private void OnRequestNavigate(object sender, RequestNavigateEventArgs e)
    {
        if (e.Uri.Scheme == Uri.UriSchemeHttps)
        {
            Process.Start(new ProcessStartInfo(e.Uri.AbsoluteUri) { UseShellExecute = true });
        }

        e.Handled = true;
    }
}
