using MaiPort.Utilities;

namespace MaiPort.ViewModels;

/// <summary>
/// メインウィンドウの集約 ViewModel。
/// </summary>
public sealed class MainViewModel : ObservableObject
{
    public MainViewModel(PortForwardViewModel forward, NetworkStatusViewModel status, OperationLogViewModel log)
    {
        Forward = forward;
        Status = status;
        Log = log;
    }

    public PortForwardViewModel Forward { get; }

    public NetworkStatusViewModel Status { get; }

    public OperationLogViewModel Log { get; }

    public string Title => "MaiPort - ポート開放ツール";

    public async Task InitializeAsync(CancellationToken ct = default)
    {
        await Forward.InitializeAsync(ct);
        await Status.RefreshAsync();
    }
}
