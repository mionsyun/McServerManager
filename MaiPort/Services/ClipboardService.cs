using System.Runtime.InteropServices;
using System.Windows;

namespace MaiPort.Services;

/// <summary>
/// WPF のクリップボード。ほかのアプリがクリップボードを掴んでいると失敗するため結果を返す。
/// </summary>
public sealed class ClipboardService : IClipboardService
{
    public bool TrySetText(string text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return false;
        }

        try
        {
            Clipboard.SetText(text);
            return true;
        }
        catch (COMException)
        {
            return false;
        }
    }
}
