using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;
using System.Security;
using System.Threading;
using System.Windows;
using McServerManager.Services.AuthoringFiles;
using McServerManager.Services.Participants;
using McServerManager.ViewModels.Authoring;

namespace McServerManager.Views;

/// <summary>Owns pickers and window lifetime. The typed model and services own validation and files.</summary>
public partial class TemplateAuthoringWindow : Window
{
    private readonly TemplateAuthoringViewModel _viewModel;
    private readonly ITemplateAuthoringFileService _files;
    private readonly List<string> _cleanupPaths = [];
    private CancellationTokenSource? _operationCancellation;
    private bool _isWorking, _closeRequested, _isClosed;

    public TemplateAuthoringWindow(TemplateAuthoringViewModel viewModel, ITemplateAuthoringFileService files)
    {
        _viewModel = viewModel ?? throw new ArgumentNullException(nameof(viewModel));
        _files = files ?? throw new ArgumentNullException(nameof(files));
        InitializeComponent();
        DataContext = _viewModel;
    }

    private void OnNew(object sender, RoutedEventArgs e)
    {
        if (_isWorking || _isClosed || !_viewModel.CanCreate || !ConfirmDiscard()) return;
        if (_viewModel.CreateNew(discardChanges: true)) ContentScroll.ScrollToTop();
    }

    public void LoadRegisteredSettings(McServerManager.Models.Authoring.RegisteredServerTemplateSource source)
    {
        if (_isWorking || _isClosed || !_viewModel.CanCreate || !ConfirmDiscard()) return;
        if (_viewModel.CreateFromRegisteredSettings(source, discardChanges: true)) ContentScroll.ScrollToTop();
    }

    private async void OnLoad(object sender, RoutedEventArgs e)
    {
        if (_isWorking || _isClosed || !_viewModel.CanLoad) return;
        var token = BeginOperation("テンプレートを選んでいます…");
        try
        {
            var dialog = new Microsoft.Win32.OpenFileDialog
            {
                Title = "コピーして編集するテンプレートを選択",
                Filter = "MaiPilot テンプレート (*.maipilot-template.json)|*.maipilot-template.json",
                DefaultExt = ".maipilot-template.json",
                CheckFileExists = true, CheckPathExists = true, Multiselect = false
            };
            if (dialog.ShowDialog(this) != true)
            {
                _viewModel.ReportOpenCanceled();
                return;
            }
            // The current draft survives a canceled picker, rejected discard, failed read or invalid input.
            if (!ConfirmDiscard()) return;
            token.ThrowIfCancellationRequested();
            OperationStatusText.Text = "テンプレートの入力形式を確認しています…";
            var bytes = await _files.ReadTemplateAsync(dialog.FileName, token);
            token.ThrowIfCancellationRequested();
            if (_isClosed) return;
            if (_viewModel.LoadForEdit(bytes, Path.GetFileName(dialog.FileName), discardChanges: true))
                ContentScroll.ScrollToTop();
            else
                _closeRequested = false;
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            if (!_isClosed) _viewModel.ReportOpenCanceled();
        }
        catch (Exception exception) when (IsExpectedFailure(exception))
        {
            if (_isClosed) return;
            _closeRequested = false;
            _viewModel.ReportOpenFailure();
        }
        finally { EndOperation(); }
    }

    private void OnAddClientMod(object sender, RoutedEventArgs e)
    {
        if (!_isWorking && !_isClosed) _viewModel.AddClientMod();
    }

    private void OnRemoveClientMod(object sender, RoutedEventArgs e)
    {
        if (!_isWorking && !_isClosed && sender is System.Windows.Controls.Button { DataContext: TemplateClientModRowViewModel row })
            _viewModel.RemoveClientMod(row);
    }

    private void OnValidate(object sender, RoutedEventArgs e)
    {
        if (_isWorking || _isClosed || !_viewModel.CanValidate) return;
        _viewModel.ValidatePreview();
        Dispatcher.BeginInvoke(new Action(() => ReviewPanel.BringIntoView()),
            System.Windows.Threading.DispatcherPriority.Loaded);
    }

    private void OnBackToEditor(object sender, RoutedEventArgs e)
    {
        if (_isWorking || _isClosed) return;
        _viewModel.BackToEditor();
        ContentScroll.ScrollToTop();
    }

    private async void OnSave(object sender, RoutedEventArgs e)
    {
        if (_isWorking || _isClosed || !_viewModel.CanExport) return;
        var revision = _viewModel.ReviewRevision;
        var token = BeginOperation("新しいテンプレートの保存先を選んでいます…");
        try
        {
            var dialog = new Microsoft.Win32.SaveFileDialog
            {
                Title = "テンプレートを新しい名前で保存",
                Filter = "MaiPilot テンプレート (*.maipilot-template.json)|*.maipilot-template.json",
                DefaultExt = ".maipilot-template.json", AddExtension = true,
                FileName = "template.maipilot-template.json", CheckPathExists = true,
                // The service refuses existing paths rather than offering to replace the original.
                OverwritePrompt = false
            };
            if (dialog.ShowDialog(this) != true)
            {
                _viewModel.ReportSaveCanceled(revision);
                return;
            }
            token.ThrowIfCancellationRequested();
            var bytes = _viewModel.CreateExportBytes();
            if (bytes is null || !_viewModel.IsCurrentExport(revision)) return;
            OperationStatusText.Text = "確認済みの内容を新しいファイルに保存しています…";
            var savedPath = await _files.SaveNewTemplateAsync(dialog.FileName, bytes, token);
            if (_isClosed) return;
            // A successful return means the no-overwrite commit happened, even after a late Cancel.
            _viewModel.ReportSaved(savedPath, revision);
            if (_closeRequested)
            {
                _closeRequested = false;
                System.Windows.MessageBox.Show(this,
                    $"中止の要求に間に合わず、テンプレートの保存が完了しました。\n保存先: {savedPath}\n\n保存先を確認してから、この画面を閉じてください。",
                    "テンプレートは保存済みです", MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            if (!_isClosed) _viewModel.ReportSaveCanceled(revision);
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
            // Keep the reason visible instead of silently closing after a failed save.
            _closeRequested = false;
            _viewModel.ReportSaveFailure(revision);
        }
        finally { EndOperation(); }
    }

    private CancellationToken BeginOperation(string status)
    {
        _isWorking = true;
        _operationCancellation = new CancellationTokenSource();
        InputControls.IsEnabled = false;
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
        InputControls.IsEnabled = true;
        AcknowledgementControls.IsEnabled = true;
        ExportControls.IsEnabled = true;
        CancelOperationButton.IsEnabled = false;
        CloseButton.IsEnabled = true;
        CloseButton.Content = "閉じる";
        BusyStatusPanel.Visibility = Visibility.Collapsed;
        if (_closeRequested)
        {
            _closeRequested = false;
            Close();
        }
    }

    private bool ConfirmDiscard() => !_viewModel.IsDirty || System.Windows.MessageBox.Show(this,
        "保存していない編集内容があります。この画面の未保存の変更を破棄しますか？\n元のテンプレートファイルは変更されません。",
        "未保存の変更", MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No) == MessageBoxResult.Yes;

    private void OnCancelOperation(object sender, RoutedEventArgs e) => RequestCancellation();
    private void OnClose(object sender, RoutedEventArgs e) => Close();

    private void RequestCancellation()
    {
        if (!_isWorking) return;
        CancelOperationButton.IsEnabled = false;
        OperationStatusText.Text = "中止を待っています。保存が完了している場合は、その結果を表示します…";
        // Preserve review/session state while the file service may already be committing.
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
        else if (!ConfirmDiscard()) e.Cancel = true;
        base.OnClosing(e);
    }

    protected override void OnClosed(EventArgs e)
    {
        _isClosed = true;
        _operationCancellation?.Cancel();
        _viewModel.Cancel();
        base.OnClosed(e);
    }

    private static bool IsExpectedFailure(Exception exception) => exception is
        IOException or UnauthorizedAccessException or ArgumentException or InvalidOperationException
        or NotSupportedException or SecurityException or Win32Exception or COMException;
}
