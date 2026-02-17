using System.Windows;

namespace McServerManager.Services;

public interface IDialogService
{
    MessageBoxResult Show(
        string message,
        string title,
        MessageBoxButton buttons = MessageBoxButton.OK,
        MessageBoxImage image = MessageBoxImage.None);
}

public sealed class DialogService : IDialogService
{
    public MessageBoxResult Show(
        string message,
        string title,
        MessageBoxButton buttons = MessageBoxButton.OK,
        MessageBoxImage image = MessageBoxImage.None)
    {
        return System.Windows.MessageBox.Show(message, title, buttons, image);
    }
}
