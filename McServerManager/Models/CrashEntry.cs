namespace McServerManager.Models;

/// <summary>
/// crash-reports フォルダ内の 1 件のクラッシュレポートを表す。
/// </summary>
public sealed class CrashEntry
{
    public string FileName { get; init; } = string.Empty;
    public string FullPath { get; init; } = string.Empty;
    public DateTime When { get; init; }
    public string Reason { get; init; } = string.Empty;

    public string WhenText => When.ToLocalTime().ToString("yyyy/MM/dd HH:mm");
}
