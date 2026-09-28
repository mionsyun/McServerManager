using System.Net;
using System.Net.Sockets;
using McServerManager.Models;

namespace McServerManager.Services;

/// <summary>
/// 統合版 (BDS) の通信方式ごとに、外部公開に必要なポートと設定上の注意点を求める。
///
/// - NetherNet (BDS 1.26.51 以降の既定): server-port は TCP の HTTP シグナリング。
///   ゲーム通信はプレイヤーごとに UDP ソケットを割り当て、NAT 越しに届く候補は
///   server-udp-ports ("[ip:]external[-external]:internal[-internal]") でしか広告できない。
/// - RakNet (旧方式): server-port / server-portv6 の UDP。
/// </summary>
public static class BedrockNetworkPlanner
{
    public const string TransportNetherNet = "nethernet";
    public const string TransportRakNet = "raknet";
    /// <summary>NetherNet の LAN ディスカバリ用ブロードキャストポート (UDP)。外部公開には不要。</summary>
    public const int LanDiscoveryPort = 7551;
    public const int RecommendedUdpPortStart = 19300;

    private static readonly Version NetherNetDefaultSince = new(1, 26, 51);

    /// <summary>transport キーがなければ、バージョンが 1.26.51 以降なら NetherNet とみなす。</summary>
    public static bool UsesNetherNet(BedrockServerProperties properties, string? installedVersion)
    {
        if (string.Equals(properties.Transport, TransportRakNet, StringComparison.OrdinalIgnoreCase))
            return false;
        if (string.Equals(properties.Transport, TransportNetherNet, StringComparison.OrdinalIgnoreCase))
            return true;
        return Version.TryParse(installedVersion, out var version) && version >= NetherNetDefaultSince;
    }

    /// <summary>ルーターで開放（転送）すべきポート。UPnP は IPv4 のみのため IPv6 ポートは含めない。</summary>
    public static IReadOnlyList<PortRequirement> GetRequiredPorts(BedrockServerProperties properties, string? installedVersion)
    {
        if (!UsesNetherNet(properties, installedVersion))
        {
            return
            [
                new(NetworkProtocol.Udp, properties.ServerPort, properties.ServerPort, properties.ServerPort, "ゲーム通信 (RakNet)")
            ];
        }

        var list = new List<PortRequirement>
        {
            new(NetworkProtocol.Tcp, properties.ServerPort, properties.ServerPort, properties.ServerPort, "接続受付 (シグナリング)")
        };
        if (TryParseUdpPorts(properties.ServerUdpPorts, out var mapping, out _) && mapping is not null)
        {
            list.Add(new(NetworkProtocol.Udp, mapping.ExternalStart, mapping.ExternalEnd, mapping.InternalStart, "ゲーム通信 (NetherNet)"));
        }

        return list;
    }

    /// <summary>外部公開を妨げそうな設定の注意点（ユーザー向け文言）。</summary>
    public static IReadOnlyList<string> GetWarnings(BedrockServerProperties properties, string? installedVersion)
    {
        var warnings = new List<string>();
        if (!UsesNetherNet(properties, installedVersion))
        {
            if (string.Equals(properties.Transport, TransportRakNet, StringComparison.OrdinalIgnoreCase))
                warnings.Add("RakNet は旧方式です。クライアント 1.26.60 で廃止予定との情報があるため、一時的な回避策として使ってください。");
            return warnings;
        }

        if (string.IsNullOrWhiteSpace(properties.ServerUdpPorts))
        {
            warnings.Add("ゲーム通信用の UDP ポート (server-udp-ports) が未設定です。LAN では遊べても、家の外から接続できない可能性があります。");
            return warnings;
        }

        if (!TryParseUdpPorts(properties.ServerUdpPorts, out var mapping, out var error) || mapping is null)
        {
            warnings.Add(error ?? "server-udp-ports の書式が正しくありません。");
            return warnings;
        }

        if (mapping.Count == 1)
            warnings.Add("UDP ポートが 1 つだけだと、同時に 1 人しか接続できません。最大人数分以上の範囲を指定してください。");
        else if (mapping.Count < properties.MaxPlayers)
            warnings.Add($"UDP ポートの範囲 ({mapping.Count} 個) が最大人数 ({properties.MaxPlayers} 人) より少ないため、全員が同時に接続できない可能性があります。");

        if (string.IsNullOrWhiteSpace(mapping.AdvertisedIp))
            warnings.Add("ルーターの内側から外部公開する場合は、先頭にグローバル IP を付けてください（付けないと外部から届く接続先が相手に伝わらない可能性があります）。");

        return warnings;
    }

    /// <summary>"[ip:]external[-external]:internal[-internal]" を解析する。空文字は「未設定」として false・error なし。</summary>
    public static bool TryParseUdpPorts(string? value, out UdpPortMapping? mapping, out string? error)
    {
        mapping = null;
        error = null;
        if (string.IsNullOrWhiteSpace(value))
            return false;

        var parts = value.Trim().Split(':');
        string? ip = null;
        string externalPart;
        string internalPart;
        switch (parts.Length)
        {
            case 2:
                externalPart = parts[0];
                internalPart = parts[1];
                break;
            case 3:
                ip = parts[0].Trim();
                externalPart = parts[1];
                internalPart = parts[2];
                break;
            default:
                error = "server-udp-ports は「[IPv4:]外部ポート[-外部ポート]:内部ポート[-内部ポート]」の形で入力してください。";
                return false;
        }

        if (ip is not null && (!IPAddress.TryParse(ip, out var address) || address.AddressFamily != AddressFamily.InterNetwork))
        {
            error = $"server-udp-ports の IP アドレス「{ip}」が正しくありません（IPv4 で指定してください）。";
            return false;
        }

        if (!TryParseRange(externalPart, out var externalStart, out var externalEnd)
            || !TryParseRange(internalPart, out var internalStart, out var internalEnd))
        {
            error = "server-udp-ports のポート番号が正しくありません（1〜65535、開始 ≦ 終了）。";
            return false;
        }

        if (externalEnd - externalStart != internalEnd - internalStart)
        {
            error = "server-udp-ports の外部と内部のポート数をそろえてください。";
            return false;
        }

        mapping = new UdpPortMapping(ip, externalStart, externalEnd, internalStart, internalEnd);
        return true;
    }

    /// <summary>推奨設定の文字列を作る（例: "203.0.113.10:19300-19319:19300-19319"）。</summary>
    public static string BuildUdpPortsValue(string? advertisedIp, int start, int count)
    {
        var end = start + Math.Max(1, count) - 1;
        var range = $"{start}-{end}";
        return string.IsNullOrWhiteSpace(advertisedIp) ? $"{range}:{range}" : $"{advertisedIp.Trim()}:{range}:{range}";
    }

    private static bool TryParseRange(string text, out int start, out int end)
    {
        start = end = 0;
        var bounds = text.Trim().Split('-');
        if (bounds.Length is < 1 or > 2
            || !int.TryParse(bounds[0], out start)
            || !int.TryParse(bounds[^1], out end))
        {
            return false;
        }

        return start is >= 1 and <= 65535 && end is >= 1 and <= 65535 && start <= end;
    }
}

/// <summary>server-udp-ports の解析結果。</summary>
public sealed record UdpPortMapping(string? AdvertisedIp, int ExternalStart, int ExternalEnd, int InternalStart, int InternalEnd)
{
    public int Count => ExternalEnd - ExternalStart + 1;
}
