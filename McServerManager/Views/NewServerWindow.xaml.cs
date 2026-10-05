using System.Diagnostics;
using System.IO;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Security;
using McServerManager.Services.Participants;
using McServerManager.ViewModels;
using System.Windows;
using System.Windows.Navigation;

namespace McServerManager.Views;

public partial class NewServerWindow : Window
{
    private readonly Func<ParticipantExportWindow>? _participantExportWindowFactory;
    private readonly IParticipantExportFileService? _files;
    private CancellationTokenSource? _templateReadCancellation;
    private NewServerViewModel? _boundViewModel;
    private bool _isInspectingTemplate, _participantReviewOpen, _isClosed;

    public NewServerWindow()
    {
        InitializeComponent();
        TemplateImportButton.IsEnabled = false;
        ParticipantReviewControls.IsEnabled = false;
        DataContextChanged += OnViewModelChanged;
    }

    public NewServerWindow(Func<ParticipantExportWindow> participantExportWindowFactory,
        IParticipantExportFileService files) : this()
    {
        _participantExportWindowFactory = participantExportWindowFactory
            ?? throw new ArgumentNullException(nameof(participantExportWindowFactory));
        _files = files ?? throw new ArgumentNullException(nameof(files));
        TemplateImportButton.IsEnabled = true;
        ParticipantReviewControls.IsEnabled = true;
    }

    private async void OnInspectTemplate(object sender, RoutedEventArgs e)
    {
        if (_isInspectingTemplate || _participantReviewOpen || _isClosed || _files is null
            || DataContext is not NewServerViewModel viewModel) return;
        _isInspectingTemplate = true;
        TemplateImportButton.IsEnabled = false;
        CancelTemplateInspectionButton.IsEnabled = true;
        TemplateReadProgress.Visibility = Visibility.Visible;
        TemplateReadStatus.Text = "テンプレートを選んでいます…";
        TemplateReadStatus.Visibility = Visibility.Visible;
        viewModel.TemplateInspection.Cancel();
        using var cancellation = new CancellationTokenSource();
        _templateReadCancellation = cancellation;
        try
        {
            var dialog = new Microsoft.Win32.OpenFileDialog
            {
                Title = "参加者向け案内を含むテンプレートを選択",
                Filter = "MaiPilot テンプレート (*.maipilot-template.json)|*.maipilot-template.json",
                CheckFileExists = true, CheckPathExists = true, Multiselect = false
            };
            if (dialog.ShowDialog(this) != true) return;
            TemplateReadStatus.Text = "テンプレートを読み込み、入力形式を確認しています…";
            var bytes = await _files.ReadTemplateAsync(dialog.FileName, cancellation.Token);
            cancellation.Token.ThrowIfCancellationRequested();
            if (_isClosed || viewModel.Route != "import") return;
            using var stream = new MemoryStream(bytes, writable: false);
            await viewModel.TemplateInspection.InspectAsync(stream, cancellation.Token);
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
            if (!_isClosed) viewModel.TemplateInspection.Cancel();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException
            or InvalidOperationException or NotSupportedException or SecurityException or Win32Exception or COMException)
        {
            if (!_isClosed && !cancellation.IsCancellationRequested) viewModel.TemplateInspection.ReportOpenFailure();
        }
        finally
        {
            _templateReadCancellation = null;
            _isInspectingTemplate = false;
            if (!_isClosed)
            {
                TemplateImportButton.IsEnabled = true;
                CancelTemplateInspectionButton.IsEnabled = false;
                TemplateReadProgress.Visibility = Visibility.Collapsed;
                TemplateReadStatus.Visibility = Visibility.Collapsed;
            }
        }
    }

    private void OnCancelTemplateInspection(object sender, RoutedEventArgs e)
    {
        _templateReadCancellation?.Cancel();
        CancelTemplateInspectionButton.IsEnabled = false;
        TemplateReadStatus.Text = "確認を取り消しています…";
        if (DataContext is NewServerViewModel viewModel) viewModel.TemplateInspection.Cancel();
    }

    private void OnViewModelChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        _templateReadCancellation?.Cancel();
        if (_boundViewModel is not null)
        {
            _boundViewModel.PropertyChanged -= OnViewModelPropertyChanged;
            _boundViewModel.TemplateInspection.Cancel();
        }
        _boundViewModel = e.NewValue as NewServerViewModel;
        if (_boundViewModel is not null) _boundViewModel.PropertyChanged += OnViewModelPropertyChanged;
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        // Cancel the bounded file read too, before inspection has started. Returning to
        // the import route must not make a stale selection current again.
        if (e.PropertyName == nameof(NewServerViewModel.Route)) _templateReadCancellation?.Cancel();
    }

    private void OnReviewTemplateParticipants(object sender, RoutedEventArgs e)
    {
        if (_isClosed || _isInspectingTemplate || _participantReviewOpen || _participantExportWindowFactory is null
            || DataContext is not NewServerViewModel viewModel || viewModel.Route != "import") return;
        var inspection = viewModel.TemplateInspection;
        if (!inspection.CanReviewParticipantDefinition || inspection.ClientDefinition is not { } definition
            || inspection.OriginalManifestSha256 is not { } originalHash) return;
        _participantReviewOpen = true;
        try
        {
            var window = _participantExportWindowFactory();
            window.LoadTemplateDefinition(definition, originalHash);
            window.Owner = this;
            window.ShowDialog();
        }
        finally { _participantReviewOpen = false; }
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

    private async void OnInspectVanillaRuntime(object sender, RoutedEventArgs e)
    {
        if (!_isClosed && !_isInspectingTemplate && !_participantReviewOpen && DataContext is NewServerViewModel { Route: "import" } viewModel)
            await viewModel.TemplateInspection.RuntimeInspection.InspectAsync();
    }

    private void OnCancelVanillaRuntime(object sender, RoutedEventArgs e)
    {
        if (DataContext is NewServerViewModel viewModel) viewModel.TemplateInspection.RuntimeInspection.Cancel();
    }

    protected override void OnClosed(EventArgs e)
    {
        _isClosed = true;
        _templateReadCancellation?.Cancel();
        DataContextChanged -= OnViewModelChanged;
        if (_boundViewModel is not null) _boundViewModel.PropertyChanged -= OnViewModelPropertyChanged;
        _boundViewModel = null;
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
