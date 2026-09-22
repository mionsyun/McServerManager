using System.Reflection;
using MaiPort.Services;
using MaiPort.Utilities;

namespace MaiPort.ViewModels;

/// <summary>
/// メインウィンドウの集約 ViewModel。
/// </summary>
public sealed class MainViewModel : ObservableObject
{
    private readonly IClipboardService _clipboard;

    public MainViewModel(
        PortForwardViewModel forward,
        NetworkStatusViewModel status,
        OperationLogViewModel log,
        BedrockSetupViewModel bedrock,
        IClipboardService clipboard)
    {
        Forward = forward;
        Status = status;
        Log = log;
        Bedrock = bedrock;
        _clipboard = clipboard;
        CopyAddressCommand = new RelayCommand(CopyAddress);
    }

    public PortForwardViewModel Forward { get; }

    public NetworkStatusViewModel Status { get; }

    public OperationLogViewModel Log { get; }

    public BedrockSetupViewModel Bedrock { get; }

    public RelayCommand CopyAddressCommand { get; }

    public string Title => "MaiPort - ポート開放ツール";

    public string VersionText => $"v{Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "1.0.0"}";

    public async Task InitializeAsync(CancellationToken ct = default)
    {
        await Forward.InitializeAsync(ct);
        await Status.RefreshAsync();
    }

    /// <summary>フレンドに知らせる「グローバルIP:ポート」をコピーする。</summary>
    private void CopyAddress()
    {
        var address = $"{Status.PublicIpText}:{Forward.PortText}";
        Log.Append(_clipboard.TrySetText(address)
            ? $"接続先をコピーしました: {address}"
            : "クリップボードにコピーできませんでした。");
    }
}
