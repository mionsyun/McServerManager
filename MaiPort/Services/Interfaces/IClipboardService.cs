namespace MaiPort.Services;

/// <summary>
/// クリップボードへのコピー。
/// </summary>
public interface IClipboardService
{
    bool TrySetText(string text);
}
