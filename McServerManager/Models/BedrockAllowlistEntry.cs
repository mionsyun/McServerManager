using System.Text.Json.Serialization;

namespace McServerManager.Models;

/// <summary>統合版 (BDS) の allowlist.json の 1 エントリ。xuid は初回参加時にサーバーが補完する。</summary>
public sealed class BedrockAllowlistEntry
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("xuid")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Xuid { get; set; }

    [JsonPropertyName("ignoresPlayerLimit")]
    public bool IgnoresPlayerLimit { get; set; }
}
