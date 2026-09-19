using System.Windows;

namespace MaiPort.Services;

/// <summary>
/// WPF の MessageBox によるダイアログ表示。
/// </summary>
public sealed class DialogService : IDialogService
{
    private const string Caption = "MaiPort";

    public void ShowInfo(string message)
    {
        MessageBox.Show(message, Caption, MessageBoxButton.OK, MessageBoxImage.Information);
    }

    public void ShowError(string message)
    {
        MessageBox.Show(message, Caption, MessageBoxButton.OK, MessageBoxImage.Warning);
    }

    public bool Confirm(string message)
    {
        return MessageBox.Show(message, Caption, MessageBoxButton.OKCancel, MessageBoxImage.Question) == MessageBoxResult.OK;
    }
}
