using McServerManager.Models;

namespace McServerManager.Services;

/// <summary>サーバーごとに必要なポートを求め、UPnP でまとめて開放・閉鎖する。</summary>
public interface IPortForwardingService
{
    /// <summary>Java 版は TCP server-port、統合版は通信方式 (NetherNet / RakNet) に応じたポート。</summary>
    PortPlan GetPlan(ServerConfig config);

    /// <summary>すべて開放できれば null、失敗したらユーザー向けメッセージを返す。</summary>
    Task<string?> OpenAsync(ServerConfig config, IReadOnlyList<PortRequirement> ports);

    Task CloseAsync(IReadOnlyList<PortRequirement> ports);
}
