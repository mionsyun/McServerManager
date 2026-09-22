using System.Collections.ObjectModel;
using MaiPort.Models;
using MaiPort.Services;
using MaiPort.Utilities;

namespace MaiPort.ViewModels;

/// <summary>
/// ポート開放・解除の入力と、開放中ポート一覧の表示を担当する。
/// ポート番号は "8211" のほか "49152-49200" の範囲指定も受け付ける。
/// </summary>
public sealed class PortForwardViewModel : ObservableObject
{
    private readonly IPortControlService _control;
    private readonly INetworkService _network;
    private readonly IDialogService _dialog;
    private readonly OperationLogViewModel _log;

    private string _portText = "8211";
    private ProtocolOption _selectedProtocol;
    private string _description = "Palworld";
    private bool _useUpnp = true;
    private bool _useFirewall = true;
    private bool _isBusy;
    private bool _isCurrentPortOpen;
    private string _statusMessage = "ポート番号とプロトコルを決めて「ポートを開放する」を押してください。";

    public PortForwardViewModel(
        IPortControlService control,
        INetworkService network,
        IDialogService dialog,
        OperationLogViewModel log)
    {
        _control = control;
        _network = network;
        _dialog = dialog;
        _log = log;
        ProtocolOptions =
        [
            new ProtocolOption(PortProtocol.Udp, "UDP"),
            new ProtocolOption(PortProtocol.Tcp, "TCP"),
            new ProtocolOption(PortProtocol.Both, "UDP + TCP")
        ];
        _selectedProtocol = ProtocolOptions[0];
        OpenCommand = new AsyncRelayCommand(OpenAsync, () => !IsBusy);
        CloseCommand = new AsyncRelayCommand(CloseCurrentAsync, () => !IsBusy);
        CheckUsageCommand = new RelayCommand(CheckUsage, () => !IsBusy);
        ApplyPresetCommand = new RelayCommand(ApplyPreset);
    }

    public IReadOnlyList<ProtocolOption> ProtocolOptions { get; }

    public IReadOnlyList<PortPreset> Presets { get; } = PresetCatalog.Presets;

    public ObservableCollection<PortRuleViewModel> Rules { get; } = [];

    public AsyncRelayCommand OpenCommand { get; }

    public AsyncRelayCommand CloseCommand { get; }

    public RelayCommand CheckUsageCommand { get; }
    public RelayCommand ApplyPresetCommand { get; }

    public string PortText
    {
        get => _portText;
        set
        {
            if (SetProperty(ref _portText, value))
            {
                UpdateCurrentState();
            }
        }
    }

    public ProtocolOption SelectedProtocol
    {
        get => _selectedProtocol;
        set
        {
            if (SetProperty(ref _selectedProtocol, value))
            {
                UpdateCurrentState();
            }
        }
    }

    public string Description
    {
        get => _description;
        set => SetProperty(ref _description, value);
    }

    public bool UseUpnp
    {
        get => _useUpnp;
        set => SetProperty(ref _useUpnp, value);
    }

    public bool UseFirewall
    {
        get => _useFirewall;
        set => SetProperty(ref _useFirewall, value);
    }

    public bool IsBusy
    {
        get => _isBusy;
        private set
        {
            if (SetProperty(ref _isBusy, value))
            {
                OpenCommand.RaiseCanExecuteChanged();
                CloseCommand.RaiseCanExecuteChanged();
                CheckUsageCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public bool IsCurrentPortOpen
    {
        get => _isCurrentPortOpen;
        private set
        {
            if (SetProperty(ref _isCurrentPortOpen, value))
            {
                OnPropertyChanged(nameof(CurrentStateText));
            }
        }
    }

    public bool IsPortValid => PortRangeParser.TryParse(PortText, out _);

    public string CurrentPortLabel => $"{PortRuleViewModel.DescribeProtocol(SelectedProtocol.Value)} {PortText}";

    public string CurrentStateText => IsCurrentPortOpen ? "開放中" : "未開放";

    public string StatusMessage
    {
        get => _statusMessage;
        private set => SetProperty(ref _statusMessage, value);
    }

    public async Task InitializeAsync(CancellationToken ct = default)
    {
        await RefreshRulesAsync(ct);
        _log.Append($"保存済みの開放記録を {Rules.Count} 件読み込みました。");
    }

    public async Task RefreshRulesAsync(CancellationToken ct = default)
    {
        var rules = await _control.GetRulesAsync(ct);
        Rules.Clear();
        foreach (var rule in rules)
        {
            Rules.Add(new PortRuleViewModel(rule, CloseRuleAsync));
        }

        UpdateCurrentState();
    }

    private void ApplyPreset(object? parameter)
    {
        if (parameter is not PortPreset preset)
        {
            return;
        }

        PortText = PortRangeParser.Format(preset.Range);
        SelectedProtocol = ProtocolOptions.First(option => option.Value == preset.Protocol);
        Description = preset.Description;
        StatusMessage = $"{preset.Name} の設定を入力しました。内容を確認して「ポートを開放する」を押してください。";
    }

    private async Task OpenAsync()
    {
        if (!TryGetRange(out var range))
        {
            return;
        }

        if (!UseUpnp && !UseFirewall)
        {
            _dialog.ShowError("「ルーター」と「Windowsファイアウォール」の少なくとも一方にチェックを入れてください。");
            return;
        }

        var protocol = SelectedProtocol.Value;
        var label = $"{PortRuleViewModel.DescribeProtocol(protocol)} {PortRangeParser.Format(range)}";
        IsBusy = true;
        StatusMessage = $"{label} を開放しています…";

        try
        {
            var outcome = await _control.OpenAsync(new PortOpenRequest(range, protocol, Description, UseUpnp, UseFirewall));
            _log.Append(outcome.Messages);
            await RefreshRulesAsync();
            StatusMessage = outcome.Success
                ? $"{label} を開放しました。"
                : $"{label} を開放できませんでした。下のログを確認してください。";
        }
        catch (Exception ex)
        {
            HandleError("ポート開放", ex);
        }
        finally
        {
            IsBusy = false;
        }
    }

    private Task CloseCurrentAsync()
    {
        return TryGetRange(out var range) ? CloseAsync(range, SelectedProtocol.Value) : Task.CompletedTask;
    }

    private Task CloseRuleAsync(PortRuleViewModel ruleViewModel)
    {
        return CloseAsync(ruleViewModel.Range, ruleViewModel.Rule.Protocol);
    }

    private async Task CloseAsync(PortRange range, PortProtocol protocol)
    {
        var label = $"{PortRuleViewModel.DescribeProtocol(protocol)} {PortRangeParser.Format(range)}";
        if (!_dialog.Confirm($"{label} の開放を解除します。よろしいですか？"))
        {
            return;
        }

        IsBusy = true;
        StatusMessage = $"{label} の開放を解除しています…";

        try
        {
            var outcome = await _control.CloseAsync(range, protocol);
            _log.Append(outcome.Messages);
            await RefreshRulesAsync();
            StatusMessage = $"{label} の開放を解除しました。";
        }
        catch (Exception ex)
        {
            HandleError("開放解除", ex);
        }
        finally
        {
            IsBusy = false;
        }
    }

    private void CheckUsage()
    {
        if (!TryGetRange(out var range))
        {
            return;
        }

        var protocol = SelectedProtocol.Value;
        var listeners = _network.GetListeners(range, protocol);
        if (listeners.Count == 0)
        {
            _log.Append($"{PortRuleViewModel.DescribeProtocol(protocol)} {PortRangeParser.Format(range)} を使用中のプロセスはありません（ゲームやサーバーを起動してから確認してください）。");
            StatusMessage = "使用中のプロセスは見つかりませんでした。";
            return;
        }
        foreach (var listener in listeners)
        {
            _log.Append($"{listener.Protocol.ToString().ToUpperInvariant()} {listener.LocalEndPoint} を {listener.ProcessName} (PID {listener.ProcessId}) が使用中です。");
        }

        StatusMessage = $"{listeners.Count} 件のプロセスが使用中です。詳しくは下のログを確認してください。";
    }

    private void UpdateCurrentState()
    {
        OnPropertyChanged(nameof(CurrentPortLabel));
        OnPropertyChanged(nameof(IsPortValid));
        IsCurrentPortOpen = PortRangeParser.TryParse(PortText, out var range)
                            && Rules.Any(item => item.Range == range && item.Rule.Protocol == SelectedProtocol.Value);
    }

    private bool TryGetRange(out PortRange range)
    {
        if (PortRangeParser.TryParse(PortText, out range))
        {
            return true;
        }

        _dialog.ShowError("ポート番号は 1〜65535 の半角数字で入力してください。範囲は「49152-49200」の形式です。");
        return false;
    }

    private void HandleError(string operationName, Exception ex)
    {
        _log.Append($"エラー: {ex.Message}");
        _dialog.ShowError($"{operationName}中に予期しないエラーが発生しました。{Environment.NewLine}{ex.Message}");
        StatusMessage = "エラーが発生しました。ログを確認してください。";
    }
}
