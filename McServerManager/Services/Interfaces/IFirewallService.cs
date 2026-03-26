using McServerManager.Models;

namespace McServerManager.Services;

public interface IFirewallService
{
    bool IsAdministrator();
    FirewallRuleInfo BuildRuleInfo(string serverId);
    void CreateRules(int port, FirewallRuleInfo info);
    void DeleteRules(FirewallRuleInfo info);
    void RecreateRules(int port, FirewallRuleInfo info);
}
