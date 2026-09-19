namespace MaiPort.Models;

/// <summary>
/// ポート操作（UPnP / ファイアウォール）の結果。
/// </summary>
public sealed record PortOperationResult(bool Success, string Message)
{
    public static PortOperationResult Ok(string message) => new(true, message);

    public static PortOperationResult Failed(string message) => new(false, message);
}
