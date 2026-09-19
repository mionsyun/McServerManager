using System.Collections.ObjectModel;
using MaiPort.Utilities;

namespace MaiPort.ViewModels;

/// <summary>
/// 画面下部の操作ログ。
/// </summary>
public sealed class OperationLogViewModel : ObservableObject
{
    private const int MaxLogLines = 200;

    public OperationLogViewModel()
    {
        ClearCommand = new RelayCommand(() => Lines.Clear());
    }

    public ObservableCollection<string> Lines { get; } = [];

    public RelayCommand ClearCommand { get; }

    public void Append(string message)
    {
        if (string.IsNullOrWhiteSpace(message))
        {
            return;
        }

        foreach (var line in message.Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries))
        {
            Lines.Add($"[{DateTime.Now:HH:mm:ss}] {line}");
        }

        while (Lines.Count > MaxLogLines)
        {
            Lines.RemoveAt(0);
        }
    }

    public void Append(IEnumerable<string> messages)
    {
        foreach (var message in messages)
        {
            Append(message);
        }
    }
}
