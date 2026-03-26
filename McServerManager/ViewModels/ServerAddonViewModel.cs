using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Windows;
using McServerManager.Models;
using McServerManager.Services;
using McServerManager.Utilities;
using OpenFileDialog = Microsoft.Win32.OpenFileDialog;
using WpfApplication = System.Windows.Application;

namespace McServerManager.ViewModels;

public sealed class ServerAddonViewModel : ObservableObject
{
    private readonly AppServices _services;
    private readonly ServerConfig _config;

    private string _addonStatus = string.Empty;
    private string _addonImportReview = "未実行";
    private string _addonSearchQuery = string.Empty;
    private string _addonCatalogStatus = "未検索";
    private AddonSearchResult? _selectedAddonSearchResult;
    private bool _isAddonBusy;
    private string _addonProgressMessage = string.Empty;
    private AddonEntry? _selectedAddon;

    public ServerAddonViewModel(AppServices services, ServerConfig config)
    {
        _services = services;
        _config = config;

        Addons = [];
        AddonSearchResults = [];

        AddAddonsCommand = new RelayCommand(
            _ => BrowseAndAddAddons(),
            _ => SupportsAddonManagement && !IsAddonBusy
        );
        RefreshAddonsCommand = new RelayCommand(
            _ => LoadAddons(),
            _ => SupportsAddonManagement && !IsAddonBusy
        );
        OpenAddonsDirectoryCommand = new RelayCommand(
            _ => OpenAddonsDirectory(),
            _ => SupportsAddonManagement && !IsAddonBusy
        );
        EnableAddonCommand = new RelayCommand(
            _ => EnableAddon(),
            _ => SelectedAddon is not null && !SelectedAddon.IsEnabled && !IsAddonBusy
        );
        DisableAddonCommand = new RelayCommand(
            _ => DisableAddon(),
            _ => SelectedAddon is not null && SelectedAddon.IsEnabled && !IsAddonBusy
        );
        DeleteAddonCommand = new RelayCommand(
            _ => DeleteAddon(),
            _ => SelectedAddon is not null && !IsAddonBusy
        );
        SearchAddonCatalogCommand = new AsyncRelayCommand(
            SearchAddonCatalogAsync,
            () => SupportsAddonManagement && !string.IsNullOrWhiteSpace(AddonSearchQuery)
        );
        OpenAddonCatalogPageCommand = new RelayCommand(
            _ => OpenSelectedAddonCatalogPage(),
            _ => SelectedAddonSearchResult is not null
                && !string.IsNullOrWhiteSpace(SelectedAddonSearchResult.ProjectUrl)
        );

        LoadAddons();
    }

    public ObservableCollection<AddonEntry> Addons { get; }
    public ObservableCollection<AddonSearchResult> AddonSearchResults { get; }

    private string ServerDirectory => _config.DirectoryPath;
    private string ServerType => _config.Type;
    private string Version => _config.Version;

    public bool SupportsAddonManagement => _services.Addons.Supports(ServerType);
    public string AddonCategoryName => _services.Addons.GetCategoryName(ServerType);

    public string AddonActiveDirectory =>
        SupportsAddonManagement
            ? _services.Addons.GetActiveDirectoryPath(ServerDirectory, ServerType)
            : "-";

    public string AddonDisabledDirectory =>
        SupportsAddonManagement
            ? _services.Addons.GetDisabledDirectoryPath(ServerDirectory, ServerType)
            : "-";

    public string AddonGuideText
    {
        get
        {
            if (string.Equals(ServerType, "Fabric", StringComparison.OrdinalIgnoreCase))
                return "Fabric: Fabric対応MOD(.jar)を追加してください。依存MOD（例: fabric-api）が必要な場合があります。";
            if (string.Equals(ServerType, "Forge", StringComparison.OrdinalIgnoreCase))
                return "Forge: Forge対応MOD(.jar)を追加してください。Fabric系は通常動作しません。";
            if (string.Equals(ServerType, "Paper", StringComparison.OrdinalIgnoreCase)
                || string.Equals(ServerType, "Purpur", StringComparison.OrdinalIgnoreCase)
                || string.Equals(ServerType, "Spigot", StringComparison.OrdinalIgnoreCase))
                return "Plugin: plugin.yml を含むプラグインを推奨します。MOD系jarは動作しない可能性があります。";
            return "この種別はMOD/プラグイン管理対象外です。";
        }
    }

    public AddonEntry? SelectedAddon
    {
        get => _selectedAddon;
        set
        {
            if (SetProperty(ref _selectedAddon, value))
            {
                EnableAddonCommand.RaiseCanExecuteChanged();
                DisableAddonCommand.RaiseCanExecuteChanged();
                DeleteAddonCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public string AddonStatus
    {
        get => _addonStatus;
        private set => SetProperty(ref _addonStatus, value);
    }

    public string AddonSearchQuery
    {
        get => _addonSearchQuery;
        set
        {
            if (SetProperty(ref _addonSearchQuery, value))
                SearchAddonCatalogCommand.RaiseCanExecuteChanged();
        }
    }

    public string AddonCatalogStatus
    {
        get => _addonCatalogStatus;
        private set => SetProperty(ref _addonCatalogStatus, value);
    }

    public AddonSearchResult? SelectedAddonSearchResult
    {
        get => _selectedAddonSearchResult;
        set
        {
            if (SetProperty(ref _selectedAddonSearchResult, value))
                OpenAddonCatalogPageCommand.RaiseCanExecuteChanged();
        }
    }

    public bool IsAddonBusy
    {
        get => _isAddonBusy;
        private set
        {
            if (SetProperty(ref _isAddonBusy, value))
                RaiseAddonCommandsCanExecuteChanged();
        }
    }

    public string AddonProgressMessage
    {
        get => _addonProgressMessage;
        private set => SetProperty(ref _addonProgressMessage, value);
    }

    public string AddonImportReview
    {
        get => _addonImportReview;
        private set => SetProperty(ref _addonImportReview, value);
    }

    public RelayCommand AddAddonsCommand { get; }
    public RelayCommand RefreshAddonsCommand { get; }
    public RelayCommand OpenAddonsDirectoryCommand { get; }
    public RelayCommand EnableAddonCommand { get; }
    public RelayCommand DisableAddonCommand { get; }
    public RelayCommand DeleteAddonCommand { get; }
    public AsyncRelayCommand SearchAddonCatalogCommand { get; }
    public RelayCommand OpenAddonCatalogPageCommand { get; }

    public void LoadAddons()
    {
        Addons.Clear();
        SelectedAddon = null;

        if (!SupportsAddonManagement)
        {
            AddonStatus = "このサーバー種別はMOD/プラグイン管理の対象外です。";
            AddonImportReview = "対象外";
            AddonCatalogStatus = "対象外";
            AddonSearchResults.Clear();
            SelectedAddonSearchResult = null;
            RaiseAddonCommandsCanExecuteChanged();
            OnPropertyChanged(nameof(SupportsAddonManagement));
            OnPropertyChanged(nameof(AddonCategoryName));
            OnPropertyChanged(nameof(AddonActiveDirectory));
            OnPropertyChanged(nameof(AddonDisabledDirectory));
            OnPropertyChanged(nameof(AddonGuideText));
            return;
        }

        try
        {
            foreach (var addon in _services.Addons.GetAddons(ServerDirectory, ServerType))
                Addons.Add(addon);

            AddonStatus =
                Addons.Count == 0
                    ? $"{AddonCategoryName}ファイルがまだありません。"
                    : $"{AddonCategoryName} {Addons.Count} 件";

            var warnings = _services.Addons.AnalyzeCompatibilityWarnings(ServerType, Addons);
            if (warnings.Count > 0)
                AddonStatus += $" / 警告 {warnings.Count} 件";

            if (string.Equals(AddonImportReview, "未実行", StringComparison.OrdinalIgnoreCase))
                AddonImportReview = "追加前チェックを実行してください。";
        }
        catch (Exception ex)
        {
            AddonStatus = "一覧取得に失敗しました。";
            _services.Dialog.Show(
                $"一覧取得に失敗しました: {ex.Message}",
                "エラー", MessageBoxButton.OK, MessageBoxImage.Error);
        }

        RaiseAddonCommandsCanExecuteChanged();
        OnPropertyChanged(nameof(SupportsAddonManagement));
        OnPropertyChanged(nameof(AddonCategoryName));
        OnPropertyChanged(nameof(AddonActiveDirectory));
        OnPropertyChanged(nameof(AddonDisabledDirectory));
        OnPropertyChanged(nameof(AddonGuideText));
    }

    private void BrowseAndAddAddons()
    {
        if (!SupportsAddonManagement)
            return;

        var dialog = new OpenFileDialog
        {
            Filter = "jarファイル|*.jar",
            Multiselect = true,
            CheckFileExists = true,
        };

        if (dialog.ShowDialog() == true)
            _ = ImportAddonsAsync(dialog.FileNames);
    }

    public async Task ImportAddonsAsync(IEnumerable<string> sourcePaths)
    {
        if (!SupportsAddonManagement || IsAddonBusy)
            return;

        try
        {
            IsAddonBusy = true;
            AddonProgressMessage = "追加前チェックを実行しています...";
            await Task.Yield();

            var assessment = _services.Addons.AssessImport(ServerType, sourcePaths, Addons);
            AddonImportReview = assessment.Summary;

            if (assessment.CandidateCount == 0)
            {
                AddonStatus = "追加できるjarファイルが見つかりませんでした。";
                return;
            }

            if (assessment.RequiresConfirmation)
            {
                var detailLines = assessment.Warnings.Take(12)
                    .Select((warning, index) => $"{index + 1}. {warning}")
                    .ToList();

                if (assessment.Warnings.Count > detailLines.Count)
                    detailLines.Add($"...他 {assessment.Warnings.Count - detailLines.Count} 件");

                var details = string.Join(Environment.NewLine, detailLines);
                var result = _services.Dialog.Show(
                    $"追加前チェックで注意点が見つかりました。続行しますか？{Environment.NewLine}{Environment.NewLine}{details}",
                    "追加前チェック", MessageBoxButton.YesNo, MessageBoxImage.Warning);

                if (result != MessageBoxResult.Yes)
                {
                    AddonStatus = "追加をキャンセルしました。";
                    return;
                }
            }

            AddonProgressMessage = "ファイルを追加しています...";
            var count = await Task.Run(() =>
                _services.Addons.AddFiles(ServerDirectory, ServerType, sourcePaths));

            AddonStatus = count > 0
                ? $"{count} 件を追加しました。"
                : "追加できるjarファイルが見つかりませんでした。";

            AddonProgressMessage = "一覧を更新しています...";
            LoadAddons();
            ShowAddonCompatibilityWarnings();
        }
        catch (Exception ex)
        {
            _services.Dialog.Show(
                $"追加に失敗しました: {ex.Message}",
                "エラー", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            AddonProgressMessage = string.Empty;
            IsAddonBusy = false;
        }
    }

    private void ShowAddonCompatibilityWarnings()
    {
        var warnings = _services.Addons.AnalyzeCompatibilityWarnings(ServerType, Addons);
        if (warnings.Count == 0)
            return;

        var lines = string.Join(
            Environment.NewLine,
            warnings.Select((warning, index) => $"{index + 1}. {warning}"));
        _services.Dialog.Show(
            $"追加した {AddonCategoryName} に互換性の注意点があります。{Environment.NewLine}{Environment.NewLine}{lines}",
            "互換性チェック", MessageBoxButton.OK, MessageBoxImage.Warning);
    }

    private void DisableAddon()
    {
        if (SelectedAddon is null)
            return;
        try
        {
            _services.Addons.Disable(ServerDirectory, ServerType, SelectedAddon);
            LoadAddons();
        }
        catch (Exception ex)
        {
            _services.Dialog.Show(
                $"無効化に失敗しました: {ex.Message}",
                "エラー", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void EnableAddon()
    {
        if (SelectedAddon is null)
            return;
        try
        {
            _services.Addons.Enable(ServerDirectory, ServerType, SelectedAddon);
            LoadAddons();
        }
        catch (Exception ex)
        {
            _services.Dialog.Show(
                $"有効化に失敗しました: {ex.Message}",
                "エラー", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void DeleteAddon()
    {
        if (SelectedAddon is null)
            return;

        if (_services.Dialog.Show(
                $"{SelectedAddon.FileName} を削除します。",
                "確認", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes)
        {
            return;
        }

        try
        {
            _services.Addons.Delete(SelectedAddon);
            LoadAddons();
        }
        catch (Exception ex)
        {
            _services.Dialog.Show(
                $"削除に失敗しました: {ex.Message}",
                "エラー", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void OpenAddonsDirectory()
    {
        if (!SupportsAddonManagement)
            return;

        var path = AddonActiveDirectory;
        try
        {
            if (string.IsNullOrWhiteSpace(path) || !Directory.Exists(path))
            {
                _services.Dialog.Show($"{AddonCategoryName}フォルダが見つかりません。", "エラー", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }
            Process.Start(new ProcessStartInfo { FileName = path, UseShellExecute = true });
        }
        catch (Exception ex)
        {
            _services.Dialog.Show(
                $"フォルダを開けませんでした: {ex.Message}",
                "エラー", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private async Task SearchAddonCatalogAsync()
    {
        if (!SupportsAddonManagement)
            return;

        var query = AddonSearchQuery?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(query))
        {
            AddonCatalogStatus = "検索キーワードを入力してください。";
            return;
        }

        try
        {
            AddonCatalogStatus = "検索中...";
            var results = await _services.AddonCatalog.SearchAsync(query, ServerType, Version, limit: 20)
                .ConfigureAwait(false);

            WpfApplication.Current.Dispatcher.Invoke(() =>
            {
                AddonSearchResults.Clear();
                foreach (var result in results)
                    AddonSearchResults.Add(result);
                SelectedAddonSearchResult = AddonSearchResults.FirstOrDefault();
            });

            AddonCatalogStatus =
                results.Count == 0
                    ? "該当する候補が見つかりませんでした。"
                    : $"{results.Count} 件見つかりました。";
        }
        catch (Exception ex)
        {
            AddonCatalogStatus = "検索に失敗しました。";
            _services.Dialog.Show(
                $"Modrinth検索に失敗しました: {ex.Message}",
                "エラー", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void OpenSelectedAddonCatalogPage()
    {
        if (SelectedAddonSearchResult is null)
            return;

        var url = SelectedAddonSearchResult.ProjectUrl;
        if (string.IsNullOrWhiteSpace(url))
            return;

        try
        {
            Process.Start(new ProcessStartInfo { FileName = url, UseShellExecute = true });
        }
        catch (Exception ex)
        {
            _services.Dialog.Show(
                $"ブラウザを開けませんでした: {ex.Message}",
                "エラー", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void RaiseAddonCommandsCanExecuteChanged()
    {
        AddAddonsCommand.RaiseCanExecuteChanged();
        RefreshAddonsCommand.RaiseCanExecuteChanged();
        OpenAddonsDirectoryCommand.RaiseCanExecuteChanged();
        EnableAddonCommand.RaiseCanExecuteChanged();
        DisableAddonCommand.RaiseCanExecuteChanged();
        DeleteAddonCommand.RaiseCanExecuteChanged();
        SearchAddonCatalogCommand.RaiseCanExecuteChanged();
        OpenAddonCatalogPageCommand.RaiseCanExecuteChanged();
    }
}
