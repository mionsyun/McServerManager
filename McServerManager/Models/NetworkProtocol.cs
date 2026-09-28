namespace McServerManager.Models;

/// <summary>ポート開放・使用中チェックで扱う通信プロトコル。Java 版は TCP、統合版は UDP。</summary>
public enum NetworkProtocol
{
    Tcp,
    Udp
}
