namespace McServerManager.Services;

/// <summary>
/// Java が無い PC 向けに、Eclipse Temurin (JRE) を MaiPilot 専用フォルダへ自動で導入する。
/// ZIP 版を展開するだけなので管理者権限は不要で、PATH などシステムの設定は変更しない。
/// </summary>
public interface IJavaRuntimeInstaller
{
    /// <summary>必要な Java のメジャー版に対して導入する LTS 版（8 / 11 / 17 / 21 / 25）。不明なら 21。</summary>
    int GetInstallMajor(int? requiredMajor);

    /// <summary>MaiPilot が導入済みの指定版の java.exe。なければ null。</summary>
    string? FindInstalled(int major);

    /// <summary>指定版の Temurin JRE を導入し、java.exe のパスを返す。導入済みならダウンロードしない。</summary>
    Task<string> InstallAsync(int major, IProgress<string>? progress = null, CancellationToken cancellationToken = default);
}
