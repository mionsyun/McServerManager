using McServerManager.Models;

namespace McServerManager.Services;

/// <summary>
/// サーバーのエディション（Java 版 / 統合版）に関する定数と判定。
/// 統合版は <see cref="ServerConfig.Type"/> が "Bedrock" のサーバーとして扱う。
/// </summary>
public static class ServerEditions
{
    public const string BedrockType = "Bedrock";
    public const string BedrockExecutableName = "bedrock_server.exe";
    public const string BedrockWorldsDirectoryName = "worlds";
    public const string BedrockDefaultLevelName = "Bedrock level";
    public const int BedrockDefaultPort = 19132;
    public const int BedrockDefaultPortV6 = 19133;
    public const int JavaDefaultPort = 25565;

    public static bool IsBedrock(string? serverType) =>
        string.Equals(serverType, BedrockType, StringComparison.OrdinalIgnoreCase);

    public static bool IsBedrock(ServerConfig config) => IsBedrock(config.Type);

    public static string GetEditionLabel(string? serverType) =>
        IsBedrock(serverType) ? "統合版" : "Java版";

    /// <summary>一覧・ヘッダー表示用の種別名（例: "統合版 (BDS)" / "Paper"）。</summary>
    public static string GetTypeDisplayName(string? serverType) =>
        IsBedrock(serverType) ? "統合版 (BDS)" : serverType ?? string.Empty;
}
