using System.IO;
using MaiPort.Models;
using MaiPort.Utilities;

namespace MaiPort.Services;

/// <summary>
/// server.properties を解析して、NetherNet / RakNet それぞれで必要なポートを導く。
/// 仕様は Bedrock Dedicated Server 付属の bedrock_server_how_to.html に準拠する。
/// </summary>
public sealed class BedrockServerService : IBedrockServerService
{
    private const string NetherNet = "nethernet";
    private const string RakNet = "raknet";
    private const int DefaultServerPort = 19132;
    private const int DefaultServerPortV6 = 19133;

    public async Task<BedrockPortPlan> LoadPlanAsync(string serverPropertiesPath, CancellationToken ct = default)
    {
        var fullPath = Path.GetFullPath(serverPropertiesPath);
        var lines = await File.ReadAllLinesAsync(fullPath, ct).ConfigureAwait(false);
        return BuildPlan(ParseProperties(lines));
    }

    public BedrockPortPlan BuildPlan(IReadOnlyDictionary<string, string> properties)
    {
        // transport の既定は nethernet（ドキュメントの Default value に準拠）。
        var transport = GetValue(properties, "transport", NetherNet).ToLowerInvariant();
        var serverPort = GetPort(properties, "server-port", DefaultServerPort);
        var items = new List<BedrockPortPlanItem>();
        var notes = new List<string>();

        if (transport == RakNet)
        {
            BuildRakNetPlan(properties, serverPort, items, notes);
        }
        else
        {
            BuildNetherNetPlan(properties, serverPort, items, notes);
        }

        return new BedrockPortPlan(transport, items, notes);
    }

    private static void BuildRakNetPlan(
        IReadOnlyDictionary<string, string> properties,
        int serverPort,
        List<BedrockPortPlanItem> items,
        List<string> notes)
    {
        var serverPortV6 = GetPort(properties, "server-portv6", DefaultServerPortV6);
        items.Add(new BedrockPortPlanItem(new PortRange(serverPort, serverPort), PortProtocol.Udp, "ゲーム通信 (RakNet / IPv4)"));
        items.Add(new BedrockPortPlanItem(new PortRange(serverPortV6, serverPortV6), PortProtocol.Udp, "ゲーム通信 (RakNet / IPv6)"));
        notes.Add("transport=raknet のため、UDP のポートだけを開放すれば接続できます。");

        var lanVisibility = GetValue(properties, "enable-lan-visibility", "true").ToLowerInvariant() != "false";
        if (lanVisibility && (serverPort != DefaultServerPort || serverPortV6 != DefaultServerPortV6))
        {
            notes.Add($"enable-lan-visibility=true のため、サーバーは既定の {DefaultServerPort}/{DefaultServerPortV6} にもバインドします（LAN 検索用。外部公開には不要）。");
        }
    }

    private static void BuildNetherNetPlan(
        IReadOnlyDictionary<string, string> properties,
        int serverPort,
        List<BedrockPortPlanItem> items,
        List<string> notes)
    {
        // NetherNet はシグナリングが TCP、ゲーム通信が UDP という2本立て。
        items.Add(new BedrockPortPlanItem(new PortRange(serverPort, serverPort), PortProtocol.Tcp, "接続開始の合図 (NetherNet シグナリング)"));

        var udpRanges = ParseUdpPorts(GetValues(properties, "server-udp-ports"));
        if (udpRanges.Count == 0)
        {
            notes.Add("server-udp-ports が未設定のため、ゲーム通信の UDP ポートは毎回変わります（OS の一時ポート）。");
            notes.Add("server.properties に「server-udp-ports=49152-49200」のように範囲を書いて固定すると、その範囲だけを開放すれば済みます。");
            return;
        }

        foreach (var range in udpRanges)
        {
            items.Add(new BedrockPortPlanItem(range, PortProtocol.Udp, "ゲーム通信 (NetherNet)"));
        }

        notes.Add("server-udp-ports で指定された UDP ポートを開放します。");
    }

    /// <summary>
    /// server-udp-ports の値を解析する。
    /// 「内部ポート」「開始-終了」「[ip:]外部:内部」のいずれの形式も、開放対象は内部ポート側。
    /// </summary>
    internal static IReadOnlyList<PortRange> ParseUdpPorts(IEnumerable<string> values)
    {
        var ranges = new List<PortRange>();
        foreach (var value in values)
        {
            foreach (var entry in value.Split(',', StringSplitOptions.RemoveEmptyEntries))
            {
                var trimmed = entry.Trim();
                if (trimmed.Length == 0)
                {
                    continue;
                }

                // IPv6 リテラルは [..] で囲まれるため、閉じ括弧より後ろだけを見る。
                var bracketEnd = trimmed.LastIndexOf(']');
                var body = bracketEnd >= 0 ? trimmed[(bracketEnd + 1)..].TrimStart(':') : trimmed;

                // 「外部:内部」形式なら最後のコロンより後ろが内部ポート。
                var colonIndex = body.LastIndexOf(':');
                var internalPart = colonIndex >= 0 ? body[(colonIndex + 1)..] : body;

                if (PortRangeParser.TryParse(internalPart, out var range))
                {
                    ranges.Add(range);
                }
            }
        }

        return ranges;
    }

    internal static IReadOnlyDictionary<string, string> ParseProperties(IEnumerable<string> lines)
    {
        var properties = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var line in lines)
        {
            var trimmed = line.Trim();
            if (trimmed.Length == 0 || trimmed.StartsWith('#'))
            {
                continue;
            }

            var separatorIndex = trimmed.IndexOf('=');
            if (separatorIndex <= 0)
            {
                continue;
            }

            var key = trimmed[..separatorIndex].Trim();
            var value = trimmed[(separatorIndex + 1)..].Trim();

            // server-udp-ports は複数行に分けて書けるため、値を連結する。
            if (properties.TryGetValue(key, out var existing) && key.Equals("server-udp-ports", StringComparison.OrdinalIgnoreCase))
            {
                properties[key] = $"{existing},{value}";
            }
            else
            {
                properties[key] = value;
            }
        }

        return properties;
    }

    private static string GetValue(IReadOnlyDictionary<string, string> properties, string key, string fallback)
    {
        return properties.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value) ? value.Trim() : fallback;
    }

    private static IEnumerable<string> GetValues(IReadOnlyDictionary<string, string> properties, string key)
    {
        return properties.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value)
            ? [value]
            : [];
    }

    private static int GetPort(IReadOnlyDictionary<string, string> properties, string key, int fallback)
    {
        return int.TryParse(GetValue(properties, key, string.Empty), out var port) && port is >= 1 and <= 65535
            ? port
            : fallback;
    }
}
