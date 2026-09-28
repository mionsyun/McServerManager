using McServerManager.Models;

namespace McServerManager.Services;

public interface IFirewallService
{
    bool IsAdministrator();
    FirewallRuleInfo BuildRuleInfo(string serverId);
    void CreateRules(int port, FirewallRuleInfo info);
    void DeleteRules(FirewallRuleInfo info);
    void RecreateRules(int port, FirewallRuleInfo info);

    /// <summary>
    /// 実行ファイル単位で受信を許可する（TCP/UDP の 2 ルール）。
    /// 統合版 (NetherNet) はゲーム通信の UDP ポートが接続ごとに変わるため、ポート指定より確実。
    /// </summary>
    void CreateProgramRules(string programPath, FirewallRuleInfo info);
    void RecreateProgramRules(string programPath, FirewallRuleInfo info);

    /// <summary>
    /// サーバーに合った受信許可ルールを作る（Java 版はポート指定、統合版は bedrock_server.exe 指定）。
    /// ルール名が未設定なら config.Firewall に採番する（保存は呼び出し側）。
    /// </summary>
    void ApplyServerRules(ServerConfig config, bool recreate);
}
