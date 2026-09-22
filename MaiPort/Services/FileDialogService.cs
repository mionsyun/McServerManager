using Microsoft.Win32;

namespace MaiPort.Services;

/// <summary>
/// server.properties を選ばせるファイルダイアログ。
/// </summary>
public sealed class FileDialogService : IFileDialogService
{
    public string? PickServerProperties()
    {
        var dialog = new OpenFileDialog
        {
            Title = "server.properties を選んでください",
            Filter = "server.properties|server.properties|プロパティファイル (*.properties)|*.properties|すべてのファイル (*.*)|*.*",
            CheckFileExists = true
        };

        return dialog.ShowDialog() == true ? dialog.FileName : null;
    }
}
