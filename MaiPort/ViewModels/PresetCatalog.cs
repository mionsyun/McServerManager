using MaiPort.Models;

namespace MaiPort.ViewModels;

/// <summary>
/// 画面に並べるプリセットの一覧。
/// </summary>
public static class PresetCatalog
{
    public static IReadOnlyList<PortPreset> Presets { get; } =
    [
        new PortPreset("Palworld", 8211, PortProtocol.Udp, "Palworld"),
        new PortPreset("Minecraft Java", 25565, PortProtocol.Both, "Minecraft Java"),
        new PortPreset("Minecraft 統合版", 19132, PortProtocol.Udp, "Minecraft Bedrock"),
        new PortPreset("ARK", 7777, PortProtocol.Udp, "ARK"),
        new PortPreset("Valheim", 2456, PortProtocol.Udp, "Valheim"),
        new PortPreset("Terraria", 7777, PortProtocol.Tcp, "Terraria")
    ];
}
