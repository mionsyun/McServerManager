using McServerManager.Services;
using McServerManager.Utilities;

namespace McServerManager.ViewModels;

/// <summary>Java の自動セットアップ（Temurin JRE の導入）の実行状態と進捗表示。</summary>
public sealed class JavaSetupViewModel : ObservableObject
{
    private readonly IJavaRuntimeInstaller _installer;
    private bool _isRunning;
    private string _status = string.Empty;

    public JavaSetupViewModel(IJavaRuntimeInstaller installer)
    {
        _installer = installer;
    }

    public bool IsRunning
    {
        get => _isRunning;
        private set => SetProperty(ref _isRunning, value);
    }

    /// <summary>画面に出す進捗・結果の文言。</summary>
    public string Status
    {
        get => _status;
        private set => SetProperty(ref _status, value);
    }

    public int GetInstallMajor(int? requiredMajor) => _installer.GetInstallMajor(requiredMajor);

    public string? FindInstalled(int major) => _installer.FindInstalled(major);

    /// <summary>Java を導入して java.exe のパスを返す。失敗時は例外を投げる（呼び出し側で案内する）。</summary>
    public async Task<string> RunAsync(int major)
    {
        if (IsRunning)
            throw new InvalidOperationException("Java のセットアップは実行中です。");

        IsRunning = true;
        try
        {
            var javaExe = await _installer.InstallAsync(major, new Progress<string>(message => Status = message));
            Status = $"Java {major} をセットアップしました。";
            return javaExe;
        }
        catch (Exception ex)
        {
            Status = $"Java のセットアップに失敗しました: {ex.Message}";
            throw;
        }
        finally
        {
            IsRunning = false;
        }
    }
}
