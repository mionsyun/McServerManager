using System.Collections.ObjectModel;
using MaiPort.Models;
using MaiPort.Services;
using MaiPort.Utilities;

namespace MaiPort.ViewModels;

/// <summary>
/// Minecraft 統合版サーバー（Bedrock）の server.properties を読み込み、
/// 必要なポートをまとめて開放する。
/// </summary>
public sealed class BedrockSetupViewModel : ObservableObject
{
    private readonly IBedrockServerService _bedrock;
    private readonly IPortControlService _control;
    private readonly IFileDialogService _fileDialog;
    private readonly IDialogService _dialog;
    private readonly OperationLogViewModel _log;
    private readonly Func<Task> _refreshRulesAsync;

    private string _summary = "server.properties を読み込むと、開放すべきポートを判定します。";
    private bool _hasPlan;
    private bool _isBusy;

    public BedrockSetupViewModel(
        IBedrockServerService bedrock,
        IPortControlService control,
        IFileDialogService fileDialog,
        IDialogService dialog,
        OperationLogViewModel log,
        Func<Task> refreshRulesAsync)
    {
        _bedrock = bedrock;
        _control = control;
        _fileDialog = fileDialog;
        _dialog = dialog;
        _log = log;
        _refreshRulesAsync = refreshRulesAsync;

        LoadCommand = new AsyncRelayCommand(LoadAsync, () => !IsBusy);
        OpenAllCommand = new AsyncRelayCommand(OpenAllAsync, () => !IsBusy && HasPlan);
    }

    public ObservableCollection<BedrockPlanItemViewModel> Items { get; } = [];

    public AsyncRelayCommand LoadCommand { get; }

    public AsyncRelayCommand OpenAllCommand { get; }

    public string Summary
    {
        get => _summary;
        private set => SetProperty(ref _summary, value);
    }

    public bool HasPlan
    {
        get => _hasPlan;
        private set
        {
            if (SetProperty(ref _hasPlan, value))
            {
                OpenAllCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public bool IsBusy
    {
        get => _isBusy;
        private set
        {
            if (SetProperty(ref _isBusy, value))
            {
                LoadCommand.RaiseCanExecuteChanged();
                OpenAllCommand.RaiseCanExecuteChanged();
            }
        }
    }

    private async Task LoadAsync()
    {
        var path = _fileDialog.PickServerProperties();
        if (string.IsNullOrWhiteSpace(path))
        {
            return;
        }

        IsBusy = true;
        try
        {
            var plan = await _bedrock.LoadPlanAsync(path);
            Items.Clear();
            foreach (var item in plan.Items)
            {
                Items.Add(new BedrockPlanItemViewModel(item));
            }

            HasPlan = Items.Count > 0;
            Summary = Items.Count > 0
                ? $"transport={plan.Transport} と判定しました。次の {Items.Count} 件を開放します。"
                : $"transport={plan.Transport} ですが、開放対象を特定できませんでした。下の注意を確認してください。";

            _log.Append($"server.properties を読み込みました: {path}");
            foreach (var item in plan.Items)
            {
                _log.Append($"  {DescribeProtocol(item.Protocol)} {PortRangeParser.Format(item.Range)} — {item.Purpose}");
            }

            _log.Append(plan.Notes);
        }
        catch (Exception ex)
        {
            _log.Append($"エラー: {ex.Message}");
            _dialog.ShowError($"server.properties を読み込めませんでした。{Environment.NewLine}{ex.Message}");
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task OpenAllAsync()
    {
        if (Items.Count == 0)
        {
            return;
        }

        var list = string.Join(Environment.NewLine, Items.Select(item => $"・{item.Label}（{item.Purpose}）"));
        if (!_dialog.Confirm($"次のポートを開放します。よろしいですか？{Environment.NewLine}{Environment.NewLine}{list}"))
        {
            return;
        }

        IsBusy = true;
        try
        {
            foreach (var planItem in Items.Select(item => item.Item))
            {
                var request = new PortOpenRequest(planItem.Range, planItem.Protocol, $"Bedrock {planItem.Purpose}", UseUpnp: true, UseFirewall: true);
                var outcome = await _control.OpenAsync(request);
                _log.Append(outcome.Messages);
            }

            await _refreshRulesAsync();
            Summary = "開放処理が終わりました。結果はログを確認してください。";
        }
        catch (Exception ex)
        {
            _log.Append($"エラー: {ex.Message}");
            _dialog.ShowError($"開放中にエラーが発生しました。{Environment.NewLine}{ex.Message}");
        }
        finally
        {
            IsBusy = false;
        }
    }

    internal static string DescribeProtocol(PortProtocol protocol)
    {
        return protocol switch
        {
            PortProtocol.Tcp => "TCP",
            PortProtocol.Udp => "UDP",
            _ => "TCP/UDP"
        };
    }
}
