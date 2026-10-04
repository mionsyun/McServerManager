using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;
using System.Security;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using McServerManager.Models.Participants;
using McServerManager.Services.Participants;
using McServerManager.ViewModels;

namespace McServerManager.Views;

/// <summary>Owns file pickers and the window lifetime; the review model and file service own the work.</summary>
public partial class ParticipantExportWindow : Window
{
    private readonly ParticipantExportViewModel _viewModel;
    private readonly IParticipantExportFileService _files;
    private readonly TemplateInspectionViewModel _templateInspection;
    private CancellationTokenSource? _operationCancellation;
    private readonly List<string> _cleanupPaths = [];
    private bool _isWorking, _closeRequested, _isClosed;

    public ParticipantExportWindow(ParticipantExportViewModel viewModel, IParticipantExportFileService files,
        TemplateInspectionViewModel templateInspection)
    {
        _viewModel = viewModel ?? throw new ArgumentNullException(nameof(viewModel));
        _files = files ?? throw new ArgumentNullException(nameof(files));
        _templateInspection = templateInspection ?? throw new ArgumentNullException(nameof(templateInspection));
        InitializeComponent();
        DataContext = _viewModel;
        TemplateStatusText.SetBinding(TextBlock.TextProperty,
            new System.Windows.Data.Binding(nameof(TemplateInspectionViewModel.ParticipantDefinitionStatus)) { Source = _templateInspection });
    }

    /// <summary>Accepts only the opaque definition returned by strict template validation.</summary>
    public void LoadTemplateDefinition(ParticipantClientDefinition definition, string originalManifestSha256)
    {
        if (_isWorking || _isClosed) throw new InvalidOperationException("別の処理が終了してから読み込んでください。");
        _templateInspection.Cancel();
        _viewModel.LoadValidatedDefinition(definition, originalManifestSha256);
        SelectedFileText.Text = "読み込み元: 形式を確認したテンプレート内のクライアント定義";
        TemplateStatusText.Visibility = Visibility.Collapsed;
    }

    private async void OnImportTemplate(object sender, RoutedEventArgs e)
    {
        if (_isWorking || _isClosed || !_viewModel.CapabilityAvailable) return;
        var token = BeginOperation("テンプレートを選んでいます…");
        _templateInspection.Cancel();
        // A new primary template selection always requires a fresh review, even if dismissed.
        _viewModel.Clear();
        SelectedFileText.Text = "ファイル未選択";
        TemplateStatusText.Visibility = Visibility.Collapsed;
        try
        {
            var dialog = new Microsoft.Win32.OpenFileDialog
            {
                Title = "参加者向け案内を含むテンプレートを選択",
                Filter = "MaiPilot テンプレート (*.maipilot-template.json)|*.maipilot-template.json",
                DefaultExt = ".maipilot-template.json",
                CheckFileExists = true, CheckPathExists = true, Multiselect = false
            };
            if (dialog.ShowDialog(this) != true)
            {
                _viewModel.ReportOpenCanceled();
                return;
            }

            TemplateStatusText.Visibility = Visibility.Visible;
            SelectedFileText.Text = $"選択したテンプレート: {Path.GetFileName(dialog.FileName)}";
            OperationStatusText.Text = "テンプレートと参加者用の定義の形式を確認しています…";
            var bytes = await _files.ReadTemplateAsync(dialog.FileName, token);
            token.ThrowIfCancellationRequested();
            if (_isClosed) return;
            using var stream = new MemoryStream(bytes, writable: false);
            await _templateInspection.InspectAsync(stream, token);
            token.ThrowIfCancellationRequested();
            if (_isClosed) return;
            if (_templateInspection.CanReviewParticipantDefinition
                && _templateInspection.ClientDefinition is { } definition
                && _templateInspection.OriginalManifestSha256 is { } originalHash)
                _viewModel.LoadValidatedDefinition(definition, originalHash);
            else
                _viewModel.ReportTemplateUnavailable(_templateInspection.Summary + "\n\n"
                    + _templateInspection.ParticipantDefinitionStatus);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            if (_isClosed) return;
            _templateInspection.Cancel();
            _viewModel.ReportOpenCanceled();
        }
        catch (Exception exception) when (IsExpectedFailure(exception))
        {
            if (_isClosed) return;
            _templateInspection.ReportOpenFailure();
            _viewModel.ReportTemplateUnavailable(_templateInspection.Summary);
        }
        finally { EndOperation(); }
    }

    private async void OnImport(object sender, RoutedEventArgs e)
    {
        if (_isWorking || _isClosed || !_viewModel.CapabilityAvailable) return;
        var token = BeginOperation("クライアント定義を選んでいます…");
        try
        {
            var dialog = new Microsoft.Win32.OpenFileDialog
            {
                Title = "参加者用のクライアント定義を選択",
                Filter = "クライアント定義 JSON (*.json)|*.json",
                DefaultExt = ".json",
                CheckFileExists = true,
                CheckPathExists = true,
                Multiselect = false
            };
            if (dialog.ShowDialog(this) != true)
            {
                _viewModel.ReportOpenCanceled();
                return;
            }

            // Never leave a previously acknowledged review available after a new selection.
            _templateInspection.Cancel();
            TemplateStatusText.Visibility = Visibility.Collapsed;
            _viewModel.Clear();
            SelectedFileText.Text = $"選択したファイル: {Path.GetFileName(dialog.FileName)}";
            OperationStatusText.Text = "ファイルを読み込み、入力形式を確認しています…";
            var bytes = await _files.ReadDefinitionAsync(dialog.FileName, token);
            if (_isClosed) return;
            await _viewModel.LoadAsync(bytes, token);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            if (_isClosed) return;
            _viewModel.ReportOpenCanceled();
        }
        catch (Exception exception) when (IsExpectedFailure(exception))
        {
            if (_isClosed) return;
            _viewModel.ReportOpenFailure();
        }
        finally { EndOperation(); }
    }

    private async void OnSave(object sender, RoutedEventArgs e)
    {
        if (_isWorking || _isClosed || !_viewModel.CanExport) return;
        var revision = _viewModel.ReviewRevision;
        var token = BeginOperation("新しい ZIP ファイルの保存先を選んでいます…");
        try
        {
            var dialog = new Microsoft.Win32.SaveFileDialog
            {
                Title = "参加者向け案内ZIPを新しい名前で保存",
                Filter = "参加者向け案内 ZIP (*.zip)|*.zip",
                DefaultExt = ".zip",
                AddExtension = true,
                FileName = "participant-guide.zip",
                CheckPathExists = true,
                // The file service rejects existing paths. Do not offer a misleading replace prompt.
                OverwritePrompt = false
            };
            if (dialog.ShowDialog(this) != true)
            {
                _viewModel.ReportSaveCanceled(revision);
                return;
            }

            OperationStatusText.Text = "確認した内容から ZIP を作成しています…";
            var bytes = await _viewModel.CreateZipAsync(token);
            if (_isClosed || bytes is null) return;
            revision = _viewModel.ReviewRevision;
            OperationStatusText.Text = "新しい ZIP ファイルに保存しています…";
            var savedPath = await _files.SaveNewZipAsync(dialog.FileName, bytes, token);
            if (_isClosed) return;

            // A successful return means the atomic, no-overwrite commit happened. A late
            // cancellation must never turn that result into a false "not saved" message.
            _viewModel.ReportSaved(savedPath, revision);
            if (_closeRequested)
            {
                _closeRequested = false;
                System.Windows.MessageBox.Show(this,
                    $"中止の要求に間に合わず、案内 ZIP の保存が完了しました。\n保存先: {savedPath}\n\n保存先を確認してから、この画面を閉じてください。",
                    "案内ZIPは保存済みです", MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            if (_isClosed) return;
            _viewModel.ReportSaveCanceled(revision);
        }
        catch (ParticipantExportCleanupException exception)
        {
            if (_isClosed) return;
            _closeRequested = false;
            _viewModel.ReportSaveFailure(revision);
            _cleanupPaths.Add(exception.StagePath);
            FileWarningPaths.Text = string.Join(Environment.NewLine, _cleanupPaths);
            FileWarningPanel.Visibility = Visibility.Visible;
            ContentScroll.ScrollToTop();
        }
        catch (Exception exception) when (IsExpectedFailure(exception))
        {
            if (_isClosed) return;
            // Keep failure visible even if Close was requested while the save was pending.
            _closeRequested = false;
            _viewModel.ReportSaveFailure(revision);
        }
        finally { EndOperation(); }
    }

    private CancellationToken BeginOperation(string status)
    {
        _isWorking = true;
        _operationCancellation = new CancellationTokenSource();
        ImportControls.IsEnabled = false;
        AcknowledgementControls.IsEnabled = false;
        ExportControls.IsEnabled = false;
        CancelOperationButton.IsEnabled = true;
        CloseButton.Content = "中止して閉じる";
        BusyStatusPanel.Visibility = Visibility.Visible;
        OperationStatusText.Text = status;
        return _operationCancellation.Token;
    }

    private void EndOperation()
    {
        _operationCancellation?.Dispose();
        _operationCancellation = null;
        _isWorking = false;
        if (_isClosed) return;
        ImportControls.IsEnabled = true;
        AcknowledgementControls.IsEnabled = true;
        ExportControls.IsEnabled = true;
        CancelOperationButton.IsEnabled = false;
        CloseButton.IsEnabled = true;
        CloseButton.Content = "閉じる";
        BusyStatusPanel.Visibility = Visibility.Collapsed;
        if (_closeRequested && !_isClosed)
        {
            _closeRequested = false;
            Close();
        }
    }

    private void OnCancelOperation(object sender, RoutedEventArgs e) => RequestCancellation();
    private void OnClose(object sender, RoutedEventArgs e) => Close();

    private void RequestCancellation()
    {
        if (!_isWorking) return;
        CancelOperationButton.IsEnabled = false;
        OperationStatusText.Text = "中止を待っています。保存が完了している場合は、その結果を表示します…";
        // Do not change the review generation while the file service may be committing.
        _operationCancellation?.Cancel();
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        if (_isWorking)
        {
            e.Cancel = true;
            _closeRequested = true;
            CloseButton.IsEnabled = false;
            RequestCancellation();
        }
        base.OnClosing(e);
    }

    protected override void OnClosed(EventArgs e)
    {
        _isClosed = true;
        _operationCancellation?.Cancel();
        _templateInspection.Cancel();
        _viewModel.Cancel();
        base.OnClosed(e);
    }

    private static bool IsExpectedFailure(Exception exception) => exception is
        IOException or UnauthorizedAccessException or ArgumentException or InvalidOperationException
        or NotSupportedException or SecurityException or Win32Exception or COMException;
}
