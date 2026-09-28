using McServerManager.Models;

namespace McServerManager.Services;

/// <summary>統合版 (Bedrock Dedicated Server) の取得・展開を担当する。</summary>
public interface IBedrockServerService
{
    /// <summary>公式配布の最新 BDS (正式版 / プレビュー) の一覧を取得する。</summary>
    Task<IReadOnlyList<BedrockVersionInfo>> GetAvailableVersionsAsync(CancellationToken cancellationToken = default);

    /// <summary>公式 URL から BDS をダウンロードしてサーバーフォルダへ展開する。既存の設定・ワールドは保持する。</summary>
    Task InstallFromUrlAsync(string downloadUrl, string serverDirectory, IProgress<string>? progress = null, CancellationToken cancellationToken = default);

    /// <summary>手元の BDS zip をサーバーフォルダへ展開する。既存の設定・ワールドは保持する。</summary>
    Task InstallFromZipAsync(string zipPath, string serverDirectory, IProgress<string>? progress = null, CancellationToken cancellationToken = default);

    bool IsInstalled(string serverDirectory);
}
