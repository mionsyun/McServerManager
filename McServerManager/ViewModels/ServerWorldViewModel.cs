using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Windows;
using McServerManager.Models;
using McServerManager.Services;
using McServerManager.Utilities;
using OpenFileDialog = Microsoft.Win32.OpenFileDialog;
using WpfApplication = System.Windows.Application;

namespace McServerManager.ViewModels;

public sealed class ServerWorldViewModel : ObservableObject
{
    private readonly AppServices _services;
    private readonly ServerConfig _config;
    private readonly ServerSettingsViewModel _settings;
    private readonly Func<ServerStatus> _getStatus;

    private string _newWorldName = string.Empty;
    private string _restoreBackupWorldName = string.Empty;
    private bool _createBackupBeforeRestore = true;
    private string _worldBackupStatus = "バックアップ未作成";
    private WorldBackupEntry? _selectedWorldBackup;
    private string _mapArchivePath = string.Empty;
    private string _mapImportWorldName = string.Empty;
    private bool _replaceWorldOnMapImport;
    private string _mapImportStatus = "配布マップ未選択";
    private ArchiveWorldCandidate? _selectedMapArchiveCandidate;
    private string? _selectedWorld;

    public ServerWorldViewModel(
        AppServices services,
        ServerConfig config,
        ServerSettingsViewModel settings,
        Func<ServerStatus> getStatus)
    {
        _services = services;
        _config = config;
        _settings = settings;
        _getStatus = getStatus;

        Worlds = [];
        WorldBackups = [];
        MapArchiveCandidates = [];

        SwitchWorldCommand = new RelayCommand(
            _ => SwitchWorld(),
            _ => !string.IsNullOrWhiteSpace(SelectedWorld) && !IsSelectedWorldCurrent
        );
        DeleteWorldCommand = new RelayCommand(
            _ => DeleteWorld(),
            _ => !string.IsNullOrWhiteSpace(SelectedWorld)
        );
        CreateWorldCommand = new RelayCommand(
            _ => CreateWorld(),
            _ => !string.IsNullOrWhiteSpace(NewWorldName)
        );
        CreateWorldBackupCommand = new AsyncRelayCommand(
            CreateWorldBackupAsync,
            CanCreateWorldBackup
        );
        RefreshWorldBackupsCommand = new RelayCommand(_ => LoadWorldBackups());
        OpenBackupsDirectoryCommand = new RelayCommand(_ => OpenBackupsDirectory());
        RestoreWorldBackupCommand = new AsyncRelayCommand(
            RestoreWorldBackupAsync,
            CanRestoreWorldBackup
        );
        DeleteWorldBackupCommand = new RelayCommand(DeleteWorldBackup, CanDeleteWorldBackup);
        RefreshWorldsCommand = new RelayCommand(_ => LoadWorlds());
        OpenSelectedWorldDirectoryCommand = new RelayCommand(
            _ => OpenSelectedWorldDirectory(),
            _ => !string.IsNullOrWhiteSpace(SelectedWorld)
        );
        OpenWorldMapCommand = new RelayCommand(
            _ => OpenWorldMap(),
            _ => !string.IsNullOrWhiteSpace(SelectedWorld)
        );
        BrowseMapArchiveCommand = new RelayCommand(_ => BrowseMapArchive());
        ImportMapArchiveCommand = new AsyncRelayCommand(ImportMapArchiveAsync, CanImportMapArchive);

        LoadWorlds();
        LoadWorldBackups();
    }

    public ObservableCollection<string> Worlds { get; }
    public ObservableCollection<WorldBackupEntry> WorldBackups { get; }
    public ObservableCollection<ArchiveWorldCandidate> MapArchiveCandidates { get; }

    public string? SelectedWorld
    {
        get => _selectedWorld;
        set
        {
            if (SetProperty(ref _selectedWorld, value))
            {
                SwitchWorldCommand.RaiseCanExecuteChanged();
                DeleteWorldCommand.RaiseCanExecuteChanged();
                OpenSelectedWorldDirectoryCommand.RaiseCanExecuteChanged();
                OpenWorldMapCommand.RaiseCanExecuteChanged();
                CreateWorldBackupCommand.RaiseCanExecuteChanged();
                OnPropertyChanged(nameof(IsSelectedWorldCurrent));
                OnPropertyChanged(nameof(WorldSwitchHint));

                if (string.IsNullOrWhiteSpace(RestoreBackupWorldName) && !string.IsNullOrWhiteSpace(value))
                {
                    RestoreBackupWorldName = value;
                }
            }
        }
    }

    public string CurrentWorldName =>
        string.IsNullOrWhiteSpace(_config.WorldName) ? "-" : _config.WorldName;

    public bool IsSelectedWorldCurrent =>
        !string.IsNullOrWhiteSpace(SelectedWorld)
        && string.Equals(SelectedWorld, _config.WorldName, StringComparison.OrdinalIgnoreCase);

    public string WorldSwitchHint
    {
        get
        {
            if (string.IsNullOrWhiteSpace(SelectedWorld))
                return "切り替え先のワールドを選択してください。";
            if (IsSelectedWorldCurrent)
                return "このワールドが現在適用中です。";
            return "切り替えは server.properties に保存され、次回再起動時に反映されます。";
        }
    }

    public string NewWorldName
    {
        get => _newWorldName;
        set
        {
            if (SetProperty(ref _newWorldName, value))
                CreateWorldCommand.RaiseCanExecuteChanged();
        }
    }

    public string RestoreBackupWorldName
    {
        get => _restoreBackupWorldName;
        set
        {
            if (SetProperty(ref _restoreBackupWorldName, value))
                RestoreWorldBackupCommand.RaiseCanExecuteChanged();
        }
    }

    public bool CreateBackupBeforeRestore
    {
        get => _createBackupBeforeRestore;
        set => SetProperty(ref _createBackupBeforeRestore, value);
    }

    public string WorldBackupStatus
    {
        get => _worldBackupStatus;
        private set => SetProperty(ref _worldBackupStatus, value);
    }

    public WorldBackupEntry? SelectedWorldBackup
    {
        get => _selectedWorldBackup;
        set
        {
            if (SetProperty(ref _selectedWorldBackup, value))
            {
                RestoreWorldBackupCommand.RaiseCanExecuteChanged();
                DeleteWorldBackupCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public string MapArchivePath
    {
        get => _mapArchivePath;
        set
        {
            if (SetProperty(ref _mapArchivePath, value))
                ImportMapArchiveCommand.RaiseCanExecuteChanged();
        }
    }

    public ArchiveWorldCandidate? SelectedMapArchiveCandidate
    {
        get => _selectedMapArchiveCandidate;
        set
        {
            if (SetProperty(ref _selectedMapArchiveCandidate, value))
            {
                OnPropertyChanged(nameof(MapArchiveSourceHint));
                ImportMapArchiveCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public string MapArchiveSourceHint
    {
        get
        {
            if (MapArchiveCandidates.Count == 0)
                return "ZIPを選択すると導入候補を表示します。";
            if (MapArchiveCandidates.Count == 1)
                return "候補は1件です。";
            return $"候補が {MapArchiveCandidates.Count} 件あります。導入元フォルダを選択してください。";
        }
    }

    public string MapImportWorldName
    {
        get => _mapImportWorldName;
        set
        {
            if (SetProperty(ref _mapImportWorldName, value))
                ImportMapArchiveCommand.RaiseCanExecuteChanged();
        }
    }

    public bool ReplaceWorldOnMapImport
    {
        get => _replaceWorldOnMapImport;
        set => SetProperty(ref _replaceWorldOnMapImport, value);
    }

    public string MapImportStatus
    {
        get => _mapImportStatus;
        private set => SetProperty(ref _mapImportStatus, value);
    }

    public RelayCommand SwitchWorldCommand { get; }
    public RelayCommand DeleteWorldCommand { get; }
    public RelayCommand CreateWorldCommand { get; }
    public AsyncRelayCommand CreateWorldBackupCommand { get; }
    public RelayCommand RefreshWorldBackupsCommand { get; }
    public RelayCommand OpenBackupsDirectoryCommand { get; }
    public AsyncRelayCommand RestoreWorldBackupCommand { get; }
    public RelayCommand DeleteWorldBackupCommand { get; }
    public RelayCommand RefreshWorldsCommand { get; }
    public RelayCommand OpenSelectedWorldDirectoryCommand { get; }
    public RelayCommand OpenWorldMapCommand { get; }
    public RelayCommand BrowseMapArchiveCommand { get; }
    public AsyncRelayCommand ImportMapArchiveCommand { get; }

    private string ServerDirectory => _config.DirectoryPath;

    /// <summary>サーバーのStatus変化時にServerViewModelから呼び出す。</summary>
    public void OnServerStatusChanged()
    {
        CreateWorldBackupCommand.RaiseCanExecuteChanged();
        RestoreWorldBackupCommand.RaiseCanExecuteChanged();
        ImportMapArchiveCommand.RaiseCanExecuteChanged();
    }

    public void LoadWorlds()
    {
        Worlds.Clear();
        foreach (var world in _services.Worlds.GetWorlds(ServerDirectory))
            Worlds.Add(world);

        if (string.IsNullOrWhiteSpace(MapImportWorldName))
            MapImportWorldName = _config.WorldName;

        if (string.IsNullOrWhiteSpace(RestoreBackupWorldName))
            RestoreBackupWorldName = _config.WorldName;

        SelectedWorld =
            Worlds.FirstOrDefault(w =>
                string.Equals(w, _config.WorldName, StringComparison.OrdinalIgnoreCase))
            ?? Worlds.FirstOrDefault();

        OnPropertyChanged(nameof(CurrentWorldName));
        OnPropertyChanged(nameof(IsSelectedWorldCurrent));
        OnPropertyChanged(nameof(WorldSwitchHint));
        SwitchWorldCommand.RaiseCanExecuteChanged();
        OpenSelectedWorldDirectoryCommand.RaiseCanExecuteChanged();
        OpenWorldMapCommand.RaiseCanExecuteChanged();
        CreateWorldBackupCommand.RaiseCanExecuteChanged();
        RestoreWorldBackupCommand.RaiseCanExecuteChanged();
        ImportMapArchiveCommand.RaiseCanExecuteChanged();
    }

    private void OpenSelectedWorldDirectory()
    {
        if (string.IsNullOrWhiteSpace(SelectedWorld))
            return;
        var path = Path.Combine(ServerDirectory, SelectedWorld);
        OpenDirectory(path, "選択したワールドフォルダが見つかりません。");
    }

    private void OpenWorldMap()
    {
        if (string.IsNullOrWhiteSpace(SelectedWorld))
            return;
        var worldPath = Path.Combine(ServerDirectory, SelectedWorld);
        var vm = new WorldMapViewModel(_services.WorldMap, worldPath);
        var window = new Views.WorldMapWindow(vm)
        {
            Owner = WpfApplication.Current?.MainWindow
        };
        window.Show();
    }

    private void CreateWorld()
    {
        try
        {
            _services.Worlds.CreateWorldFolder(ServerDirectory, NewWorldName.Trim());
            NewWorldName = string.Empty;
            LoadWorlds();
        }
        catch (Exception ex)
        {
            _services.Dialog.Show(
                $"ワールド作成に失敗しました: {ex.Message}",
                "エラー", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void SwitchWorld()
    {
        if (string.IsNullOrWhiteSpace(SelectedWorld))
            return;

        var props = _settings.ToModel();
        props.LevelName = SelectedWorld;
        _services.Properties.Save(ServerDirectory, props);
        _config.WorldName = SelectedWorld;
        _services.Configs.Save(_config);
        _settings.Load(props);

        OnPropertyChanged(nameof(CurrentWorldName));
        OnPropertyChanged(nameof(IsSelectedWorldCurrent));
        OnPropertyChanged(nameof(WorldSwitchHint));
        SwitchWorldCommand.RaiseCanExecuteChanged();
    }

    private void DeleteWorld()
    {
        if (string.IsNullOrWhiteSpace(SelectedWorld))
            return;

        if (_getStatus() != ServerStatus.Stopped)
        {
            _services.Dialog.Show(
                "停止中のみ削除できます。",
                "確認", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        if (_services.Dialog.Show(
                $"ワールド {SelectedWorld} を削除します。",
                "確認", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes)
        {
            return;
        }

        try
        {
            _services.Worlds.DeleteWorld(ServerDirectory, SelectedWorld);
            if (string.Equals(_config.WorldName, SelectedWorld, StringComparison.OrdinalIgnoreCase))
            {
                _config.WorldName = "world";
                _services.Configs.Save(_config);
                OnPropertyChanged(nameof(CurrentWorldName));
            }
            LoadWorlds();
        }
        catch (Exception ex)
        {
            _services.Dialog.Show(
                $"削除に失敗しました: {ex.Message}",
                "エラー", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    public void LoadWorldBackups()
    {
        var previousSelectedPath = SelectedWorldBackup?.FullPath;
        WorldBackups.Clear();
        foreach (var backup in _services.Worlds.GetWorldBackups(ServerDirectory))
            WorldBackups.Add(backup);

        if (!string.IsNullOrWhiteSpace(previousSelectedPath))
        {
            SelectedWorldBackup = WorldBackups.FirstOrDefault(entry =>
                string.Equals(entry.FullPath, previousSelectedPath, StringComparison.OrdinalIgnoreCase));
        }

        SelectedWorldBackup ??= WorldBackups.FirstOrDefault();

        if (WorldBackups.Count == 0)
            WorldBackupStatus = "バックアップはまだありません。";
        else if (string.IsNullOrWhiteSpace(WorldBackupStatus) || WorldBackupStatus == "バックアップ未作成")
            WorldBackupStatus = $"バックアップ {WorldBackups.Count} 件";

        RestoreWorldBackupCommand.RaiseCanExecuteChanged();
        DeleteWorldBackupCommand.RaiseCanExecuteChanged();
    }

    private bool CanCreateWorldBackup() =>
        _getStatus() == ServerStatus.Stopped && !string.IsNullOrWhiteSpace(SelectedWorld);

    private async Task CreateWorldBackupAsync()
    {
        if (_getStatus() != ServerStatus.Stopped)
        {
            _services.Dialog.Show(
                "停止中のみバックアップできます。",
                "確認", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var worldName = SelectedWorld?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(worldName))
        {
            _services.Dialog.Show(
                "バックアップ対象のワールドを選択してください。",
                "確認", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        try
        {
            WorldBackupStatus = "バックアップを作成しています...";
            var created = await Task.Run(() =>
                _services.Worlds.CreateWorldBackup(ServerDirectory, worldName));

            LoadWorldBackups();
            SelectedWorldBackup = WorldBackups.FirstOrDefault(entry =>
                string.Equals(entry.FullPath, created.FullPath, StringComparison.OrdinalIgnoreCase));
            WorldBackupStatus = $"バックアップ作成完了: {created.FileName}";
        }
        catch (Exception ex)
        {
            WorldBackupStatus = "バックアップ作成に失敗しました。";
            _services.Dialog.Show(
                $"バックアップ作成に失敗しました: {ex.Message}",
                "エラー", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private bool CanRestoreWorldBackup() =>
        _getStatus() == ServerStatus.Stopped
        && SelectedWorldBackup is not null
        && !string.IsNullOrWhiteSpace(RestoreBackupWorldName);

    private async Task RestoreWorldBackupAsync()
    {
        if (_getStatus() != ServerStatus.Stopped)
        {
            _services.Dialog.Show(
                "停止中のみ復元できます。",
                "確認", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var backup = SelectedWorldBackup;
        var targetWorldName = RestoreBackupWorldName?.Trim() ?? string.Empty;
        if (backup is null || string.IsNullOrWhiteSpace(targetWorldName))
        {
            WorldBackupStatus = "復元先ワールド名とバックアップを指定してください。";
            return;
        }

        var targetDirectory = Path.Combine(ServerDirectory, targetWorldName);
        if (Directory.Exists(targetDirectory))
        {
            var result = _services.Dialog.Show(
                $"ワールド {targetWorldName} をバックアップ {backup.FileName} で上書き復元します。続行しますか？",
                "確認", MessageBoxButton.YesNo, MessageBoxImage.Warning);
            if (result != MessageBoxResult.Yes)
            {
                WorldBackupStatus = "復元をキャンセルしました。";
                return;
            }
        }

        try
        {
            WorldBackupStatus = "バックアップを復元しています...";
            var sourceWorld = await Task.Run(() =>
                _services.Worlds.RestoreWorldBackup(
                    ServerDirectory,
                    backup.FullPath,
                    targetWorldName,
                    overwriteExisting: true,
                    createBackupBeforeRestore: CreateBackupBeforeRestore));

            LoadWorlds();
            LoadWorldBackups();
            SelectedWorld = targetWorldName;
            SwitchWorld();
            WorldBackupStatus = $"復元完了: {backup.FileName} -> {targetWorldName}";
            _services.Dialog.Show(
                $"バックアップ ({backup.FileName}) を {targetWorldName} に復元しました。導入元: {sourceWorld}",
                "完了", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            WorldBackupStatus = "復元に失敗しました。";
            _services.Dialog.Show(
                $"バックアップ復元に失敗しました: {ex.Message}",
                "エラー", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private bool CanDeleteWorldBackup() => SelectedWorldBackup is not null;

    private void DeleteWorldBackup()
    {
        var backup = SelectedWorldBackup;
        if (backup is null)
            return;

        if (_services.Dialog.Show(
                $"バックアップ {backup.FileName} を削除します。",
                "確認", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes)
        {
            return;
        }

        try
        {
            _services.Worlds.DeleteWorldBackup(backup.FullPath);
            LoadWorldBackups();
            WorldBackupStatus = $"バックアップを削除しました: {backup.FileName}";
        }
        catch (Exception ex)
        {
            _services.Dialog.Show(
                $"バックアップ削除に失敗しました: {ex.Message}",
                "エラー", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void OpenBackupsDirectory()
    {
        var backupsDirectory = _services.Worlds.GetBackupsDirectory(ServerDirectory);
        Directory.CreateDirectory(backupsDirectory);
        OpenDirectory(backupsDirectory, "バックアップフォルダが見つかりません。");
    }

    private bool CanImportMapArchive() =>
        _getStatus() == ServerStatus.Stopped
        && !string.IsNullOrWhiteSpace(MapArchivePath)
        && File.Exists(MapArchivePath)
        && !string.IsNullOrWhiteSpace(MapImportWorldName)
        && SelectedMapArchiveCandidate is not null;

    private void BrowseMapArchive()
    {
        var dialog = new OpenFileDialog
        {
            Filter = "ZIP ファイル|*.zip",
            CheckFileExists = true,
            Multiselect = false,
        };

        if (dialog.ShowDialog() != true)
            return;

        MapArchivePath = dialog.FileName;
        LoadMapArchiveCandidates(dialog.FileName);
    }

    private async Task ImportMapArchiveAsync()
    {
        if (_getStatus() != ServerStatus.Stopped)
        {
            _services.Dialog.Show(
                "停止中のみ配布マップを導入できます。",
                "確認", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var archivePath = MapArchivePath?.Trim() ?? string.Empty;
        var worldName = MapImportWorldName?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(archivePath) || string.IsNullOrWhiteSpace(worldName))
        {
            MapImportStatus = "ZIP と導入先ワールド名を指定してください。";
            return;
        }

        if (!File.Exists(archivePath))
        {
            MapImportStatus = "ZIP ファイルが見つかりません。";
            return;
        }

        var selectedCandidate = SelectedMapArchiveCandidate;
        if (selectedCandidate is null)
        {
            MapImportStatus = "導入元フォルダを選択してください。";
            return;
        }

        if (!_settings.EnableCommandBlock)
        {
            var result = _services.Dialog.Show(
                "配布マップにはコマンドブロックが必要な場合があります。\n有効にしますか？（有効にしない場合、正常に動作しない恐れがあります）",
                "コマンドブロック設定", MessageBoxButton.YesNoCancel, MessageBoxImage.Warning);

            if (result == MessageBoxResult.Cancel)
            {
                MapImportStatus = "導入をキャンセルしました。";
                return;
            }

            if (result == MessageBoxResult.Yes)
            {
                _settings.EnableCommandBlock = true;
                SaveSettingsInternal();
            }
        }

        var targetDirectory = Path.Combine(ServerDirectory, worldName);
        if (Directory.Exists(targetDirectory) && !ReplaceWorldOnMapImport)
        {
            MapImportStatus = "同名ワールドが存在します。上書き設定を有効にしてください。";
            _services.Dialog.Show(
                $"ワールド {worldName} は既に存在します。上書きして導入する場合はチェックを有効にしてください。",
                "確認", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        if (Directory.Exists(targetDirectory) && ReplaceWorldOnMapImport)
        {
            var confirm = _services.Dialog.Show(
                $"ワールド {worldName} は既に存在します。上書きして導入しますか？",
                "確認", MessageBoxButton.YesNo, MessageBoxImage.Warning);
            if (confirm != MessageBoxResult.Yes)
            {
                MapImportStatus = "導入をキャンセルしました。";
                return;
            }
        }

        try
        {
            MapImportStatus = "配布マップを導入しています...";
            var importedSource = await Task.Run(() =>
                _services.Worlds.ImportWorldArchive(
                    ServerDirectory,
                    archivePath,
                    worldName,
                    ReplaceWorldOnMapImport,
                    selectedCandidate.RelativePath));

            LoadWorlds();
            SelectedWorld = worldName;
            SwitchWorld();

            MapImportStatus = $"導入完了: {worldName}";
            _services.Dialog.Show(
                $"配布マップ ({importedSource}) を {worldName} として導入しました。次回起動時にこのワールドが適用されます。",
                "完了", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            MapImportStatus = "導入に失敗しました。";
            _services.Dialog.Show(
                $"配布マップ導入に失敗しました: {ex.Message}",
                "エラー", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private bool LoadMapArchiveCandidates(string archivePath)
    {
        try
        {
            MapArchiveCandidates.Clear();
            SelectedMapArchiveCandidate = null;

            foreach (var candidate in _services.Worlds.GetArchiveWorldCandidates(archivePath))
                MapArchiveCandidates.Add(candidate);

            SelectedMapArchiveCandidate = MapArchiveCandidates.FirstOrDefault();
            OnPropertyChanged(nameof(MapArchiveSourceHint));

            if (MapArchiveCandidates.Count == 0)
            {
                MapImportStatus = "ZIP内にワールドデータが見つかりません。";
                _services.Dialog.Show(
                    "ZIP内に導入可能なワールドが見つかりませんでした。level.dat または region を含むワールドを選択してください。",
                    "確認", MessageBoxButton.OK, MessageBoxImage.Warning);
                return false;
            }

            if (string.IsNullOrWhiteSpace(MapImportWorldName))
            {
                MapImportWorldName = SuggestWorldNameFromCandidate(
                    SelectedMapArchiveCandidate, archivePath);
            }

            MapImportStatus =
                MapArchiveCandidates.Count == 1
                    ? "導入元フォルダを自動選択しました。"
                    : $"導入元候補が {MapArchiveCandidates.Count} 件見つかりました。正しいフォルダを選択してください。";
            return true;
        }
        catch (Exception ex)
        {
            MapArchiveCandidates.Clear();
            SelectedMapArchiveCandidate = null;
            OnPropertyChanged(nameof(MapArchiveSourceHint));
            MapImportStatus = "ZIPの解析に失敗しました。";
            _services.Dialog.Show(
                $"配布マップZIPの解析に失敗しました: {ex.Message}",
                "エラー", MessageBoxButton.OK, MessageBoxImage.Error);
            return false;
        }
    }

    private static string SuggestWorldNameFromCandidate(ArchiveWorldCandidate? candidate, string archivePath)
    {
        if (candidate is not null && !string.IsNullOrWhiteSpace(candidate.RelativePath))
        {
            var name = candidate.RelativePath
                .Split(['/', '\\'], StringSplitOptions.RemoveEmptyEntries)
                .LastOrDefault();
            var sanitized = SanitizeWorldName(name ?? string.Empty);
            if (!string.IsNullOrWhiteSpace(sanitized))
                return sanitized;
        }
        return SuggestWorldNameFromArchive(archivePath);
    }

    private static string SuggestWorldNameFromArchive(string archivePath) =>
        SanitizeWorldName(Path.GetFileNameWithoutExtension(archivePath));

    private static string SanitizeWorldName(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return "world";
        var cleaned = new string([.. raw.Where(ch => !Path.GetInvalidFileNameChars().Contains(ch))]).Trim();
        return string.IsNullOrWhiteSpace(cleaned) ? "world" : cleaned;
    }

    /// <summary>ImportMapArchive から EnableCommandBlock を有効化する際に使う軽量な保存ヘルパー。</summary>
    private void SaveSettingsInternal()
    {
        var props = _settings.ToModel();
        _services.Properties.Save(ServerDirectory, props);
        _config.WorldName = props.LevelName;
        _services.Configs.Save(_config);
        _settings.Load(props);
    }

    private void OpenDirectory(string path, string missingMessage)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(path) || !Directory.Exists(path))
            {
                _services.Dialog.Show(missingMessage, "エラー", MessageBoxButton.OK, MessageBoxImage.Error);
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
}
