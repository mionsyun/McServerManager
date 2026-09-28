using System.Collections.ObjectModel;
using System.Text.RegularExpressions;
using McServerManager.Models;
using McServerManager.Services;
using McServerManager.Utilities;

namespace McServerManager.ViewModels;

/// <summary>
/// 統合版のプレイヤー管理（allowlist.json と OP 付与）。
/// BDS の OP は permissions.json が XUID 必須のため、稼働中のコンソールコマンド (op / deop) で付与する。
/// </summary>
public sealed class BedrockPlayersViewModel : ObservableObject
{
    // Xbox ゲーマータグは英数字とスペースのみ。コマンド注入を防ぐため引用符や改行は受け付けない。
    private static readonly Regex GamertagRegex = new(@"^[A-Za-z0-9][A-Za-z0-9 _\-]{0,31}$", RegexOptions.Compiled);

    private readonly IBedrockPropertiesService _properties;
    private readonly string _serverDirectory;
    private readonly Func<bool> _isRunning;
    private readonly Action<string> _sendCommand;

    private string _newAllowlistName = string.Empty;
    private BedrockAllowlistEntry? _selectedAllowlistEntry;
    private string _operatorName = string.Empty;
    private string _statusMessage = string.Empty;

    public BedrockPlayersViewModel(
        IBedrockPropertiesService properties,
        string serverDirectory,
        Func<bool> isRunning,
        Action<string> sendCommand)
    {
        _properties = properties;
        _serverDirectory = serverDirectory;
        _isRunning = isRunning;
        _sendCommand = sendCommand;

        Allowlist = [];
        AddAllowlistCommand = new RelayCommand(_ => AddAllowlist(), _ => !string.IsNullOrWhiteSpace(NewAllowlistName));
        RemoveAllowlistCommand = new RelayCommand(_ => RemoveAllowlist(), _ => SelectedAllowlistEntry is not null);
        GrantOperatorCommand = new RelayCommand(_ => SendOperatorCommand("op"), _ => CanSendOperatorCommand());
        RevokeOperatorCommand = new RelayCommand(_ => SendOperatorCommand("deop"), _ => CanSendOperatorCommand());
        ReloadCommand = new RelayCommand(_ => Load());
    }

    public ObservableCollection<BedrockAllowlistEntry> Allowlist { get; }

    public string NewAllowlistName
    {
        get => _newAllowlistName;
        set
        {
            if (SetProperty(ref _newAllowlistName, value))
                AddAllowlistCommand.RaiseCanExecuteChanged();
        }
    }

    public BedrockAllowlistEntry? SelectedAllowlistEntry
    {
        get => _selectedAllowlistEntry;
        set
        {
            if (SetProperty(ref _selectedAllowlistEntry, value))
                RemoveAllowlistCommand.RaiseCanExecuteChanged();
        }
    }

    public string OperatorName
    {
        get => _operatorName;
        set
        {
            if (SetProperty(ref _operatorName, value))
                RefreshOperatorCommands();
        }
    }

    public string StatusMessage
    {
        get => _statusMessage;
        private set => SetProperty(ref _statusMessage, value);
    }

    public string AllowlistSummary => Allowlist.Count == 0 ? "登録なし" : $"{Allowlist.Count} 人";

    public RelayCommand AddAllowlistCommand { get; }
    public RelayCommand RemoveAllowlistCommand { get; }
    public RelayCommand GrantOperatorCommand { get; }
    public RelayCommand RevokeOperatorCommand { get; }
    public RelayCommand ReloadCommand { get; }

    public void Load()
    {
        Allowlist.Clear();
        try
        {
            foreach (var entry in _properties.LoadAllowlist(_serverDirectory))
                Allowlist.Add(entry);
        }
        catch (Exception ex)
        {
            StatusMessage = $"allowlist.json の読み込みに失敗しました: {ex.Message}";
        }
        OnPropertyChanged(nameof(AllowlistSummary));
    }

    /// <summary>稼働状態が変わったときに ServerViewModel から呼ぶ（OP ボタンの有効/無効を更新）。</summary>
    public void RefreshOperatorCommands()
    {
        GrantOperatorCommand.RaiseCanExecuteChanged();
        RevokeOperatorCommand.RaiseCanExecuteChanged();
    }

    private void AddAllowlist()
    {
        var name = NewAllowlistName.Trim();
        if (!GamertagRegex.IsMatch(name))
        {
            StatusMessage = "ゲーマータグは英数字・スペースで入力してください。";
            return;
        }

        if (Allowlist.Any(e => string.Equals(e.Name, name, StringComparison.OrdinalIgnoreCase)))
        {
            StatusMessage = $"「{name}」は登録済みです。";
            return;
        }

        Allowlist.Add(new BedrockAllowlistEntry { Name = name });
        if (SaveAllowlist())
        {
            NewAllowlistName = string.Empty;
            StatusMessage = $"✓ 「{name}」を参加許可リストに追加しました";
        }
    }

    private void RemoveAllowlist()
    {
        if (SelectedAllowlistEntry is null)
            return;
        var name = SelectedAllowlistEntry.Name;
        Allowlist.Remove(SelectedAllowlistEntry);
        if (SaveAllowlist())
            StatusMessage = $"✓ 「{name}」を参加許可リストから削除しました";
    }

    private bool SaveAllowlist()
    {
        try
        {
            _properties.SaveAllowlist(_serverDirectory, Allowlist);
            // 稼働中はファイルを再読込させる（再起動なしで反映）
            if (_isRunning())
                _sendCommand("allowlist reload");
            OnPropertyChanged(nameof(AllowlistSummary));
            return true;
        }
        catch (Exception ex)
        {
            StatusMessage = $"allowlist.json の保存に失敗しました: {ex.Message}";
            Load();
            return false;
        }
    }

    private bool CanSendOperatorCommand() =>
        _isRunning() && !string.IsNullOrWhiteSpace(OperatorName);

    private void SendOperatorCommand(string command)
    {
        var name = OperatorName.Trim();
        if (!GamertagRegex.IsMatch(name))
        {
            StatusMessage = "ゲーマータグは英数字・スペースで入力してください。";
            return;
        }

        _sendCommand($"{command} \"{name}\"");
        StatusMessage = command == "op"
            ? $"✓ 「{name}」に OP を付与しました（参加中のプレイヤーのみ有効）"
            : $"✓ 「{name}」の OP を解除しました";
        OperatorName = string.Empty;
    }
}
