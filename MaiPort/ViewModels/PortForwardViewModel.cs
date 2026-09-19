using System.Collections.ObjectModel;
using MaiPort.Models;
using MaiPort.Services;
using MaiPort.Utilities;

namespace MaiPort.ViewModels;

/// <summary>
/// ポート開放・解除の入力と、開放中ポート一覧の表示を担当する。
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
    private string _statusMessage = "ポート番号とプロトコルを指定して「開放する」を押してください。";

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
            new ProtocolOption(PortProtocol.Udp, "UDP のみ"),
            new ProtocolOption(PortProtocol.Tcp, "TCP のみ"),
            new ProtocolOption(PortProtocol.Both, "TCP と UDP の両方")
        ];
        _selectedProtocol = ProtocolOptions[0];

        OpenCommand = new AsyncRelayCommand(OpenAsync, () => !IsBusy);
        CloseCommand = new AsyncRelayCommand(CloseCurrentAsync, () => !IsBusy);
        CheckUsageCommand = new RelayCommand(CheckUsage, () => !IsBusy);
        ApplyPalworldPresetCommand = new RelayCommand(ApplyPalworldPreset);
    }

    public IReadOnlyList<ProtocolOption> ProtocolOptions { get; }

    public ObservableCollection<PortRuleViewModel> Rules { get; } = [];

    public AsyncRelayCommand OpenCommand { get; }

    public AsyncRelayCommand CloseCommand { get; }

    public RelayCommand CheckUsageCommand { get; }

    public RelayCommand ApplyPalworldPresetCommand { get; }

    public string PortText
    {
        get => _portText;
        set => SetProperty(ref _portText, value);
    }

    public ProtocolOption SelectedProtocol
    {
        get => _selectedProtocol;
        set => SetProperty(ref _selectedProtocol, value);
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

    private void ApplyPalworldPreset()
    {
        PortText = "8211";
        SelectedProtocol = ProtocolOptions.First(option => option.Value == PortProtocol.Udp);
        Description = "Palworld";
        _log.Append("プリセットを適用しました: Palworld (UDP 8211)");
    }

    private async Task OpenAsync()
    {
        if (!TryGetPort(out var port))
        {
            return;
        }

        if (!UseUpnp && !UseFirewall)
        {
            _dialog.ShowError("UPnP とファイアウォールの少なくとも一方を選択してください。");
            return;
        }

        var protocol = SelectedProtocol.Value;
        var label = $"{PortRuleViewModel.DescribeProtocol(protocol)} {port}";
        IsBusy = true;
        StatusMessage = $"{label} を開放しています…";

        try
        {
            var request = new PortOpenRequest(port, protocol, Description, UseUpnp, UseFirewall);
            var outcome = await _control.OpenAsync(request);
            _log.Append(outcome.Messages);
            await RefreshRulesAsync();
            StatusMessage = outcome.Success
                ? $"{label} の開放処理が完了しました。"
                : $"{label} を開放できませんでした。ログを確認してください。";
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
        return TryGetPort(out var port) ? CloseAsync(port, SelectedProtocol.Value) : Task.CompletedTask;
    }

    private Task CloseRuleAsync(PortRuleViewModel ruleViewModel)
    {
        return CloseAsync(ruleViewModel.Rule.Port, ruleViewModel.Rule.Protocol);
    }

    private async Task CloseAsync(int port, PortProtocol protocol)
    {
        var label = $"{PortRuleViewModel.DescribeProtocol(protocol)} {port}";
        if (!_dialog.Confirm($"{label} の開放を解除します。よろしいですか？"))
        {
            return;
        }

        IsBusy = true;
        StatusMessage = $"{label} の開放を解除しています…";

        try
        {
            var outcome = await _control.CloseAsync(port, protocol);
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
        if (!TryGetPort(out var port))
        {
            return;
        }

        var protocol = SelectedProtocol.Value;
        var listeners = _network.GetListeners(port, protocol);
        if (listeners.Count == 0)
        {
            _log.Append($"{PortRuleViewModel.DescribeProtocol(protocol)} {port} を使用中のプロセスはありません（ゲームやサーバーを起動してから確認してください）。");
            return;
        }

        foreach (var listener in listeners)
        {
            _log.Append($"{listener.Protocol.ToString().ToUpperInvariant()} {listener.LocalEndPoint} を {listener.ProcessName} (PID {listener.ProcessId}) が使用中です。");
        }
    }

    private async Task RefreshRulesAsync(CancellationToken ct = default)
    {
        var rules = await _control.GetRulesAsync(ct);
        Rules.Clear();
        foreach (var rule in rules)
        {
            Rules.Add(new PortRuleViewModel(rule, CloseRuleAsync));
        }
    }

    private bool TryGetPort(out int port)
    {
        if (int.TryParse(PortText?.Trim(), out port) && port is >= 1 and <= 65535)
        {
            return true;
        }

        _dialog.ShowError("ポート番号は 1〜65535 の半角数字で入力してください。");
        port = 0;
        return false;
    }

    private void HandleError(string operationName, Exception ex)
    {
        _log.Append($"エラー: {ex.Message}");
        _dialog.ShowError($"{operationName}中に予期しないエラーが発生しました。{Environment.NewLine}{ex.Message}");
        StatusMessage = "エラーが発生しました。";
    }
}
