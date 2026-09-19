namespace MaiPort.Services;

/// <summary>
/// ユーザーへの通知・確認ダイアログ。
/// </summary>
public interface IDialogService
{
    void ShowInfo(string message);

    void ShowError(string message);

    bool Confirm(string message);
}
