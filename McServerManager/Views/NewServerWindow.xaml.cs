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

    private void OnSelectInspectionMods(object sender, RoutedEventArgs e) => SelectModFiles(false);
    private void OnSelectInstalledMods(object sender, RoutedEventArgs e) => SelectModFiles(true);
    private void SelectModFiles(bool installed)
    {
        if (DataContext is not NewServerViewModel viewModel) return;
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Title = installed ? "導入済みとして比較するMOD" : "確認するMOD",
            Filter = "Java MOD (*.jar)|*.jar", CheckFileExists = true, Multiselect = true
        };
        if (dialog.ShowDialog(this) != true) return;
        if (dialog.FileNames.Length > 256) { viewModel.ModInspection.ReportFileSelectionFailure(); return; }
        if (installed) viewModel.ModInspection.SetInstalledFiles(dialog.FileNames);
        else viewModel.ModInspection.SetSelectedFiles(dialog.FileNames);
    }
    private void OnClearInspectionMods(object sender, RoutedEventArgs e)
    {
        if (DataContext is NewServerViewModel viewModel) viewModel.ModInspection.ClearFiles();
    }
    private async void OnInspectMods(object sender, RoutedEventArgs e)
    {
        if (DataContext is NewServerViewModel viewModel && !viewModel.ModInspection.IsBusy)
            await viewModel.ModInspection.InspectAsync();
    }
    private void OnCancelModInspection(object sender, RoutedEventArgs e)
    {
        if (DataContext is NewServerViewModel viewModel) viewModel.ModInspection.Cancel();
    }

    protected override void OnClosed(EventArgs e)
    {
        if (DataContext is NewServerViewModel viewModel)
        {
            viewModel.TemplateInspection.Cancel();
            viewModel.ModInspection.Cancel();
        }
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
