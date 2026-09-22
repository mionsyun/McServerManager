using MaiPort.Models;

namespace MaiPort.ViewModels;

/// <summary>
/// 画面に並べるプリセットの一覧。
/// </summary>
public static class PresetCatalog
{
    public static IReadOnlyList<PortPreset> Presets { get; } =
    [
        new PortPreset("Palworld", new PortRange(8211, 8211), PortProtocol.Udp, "Palworld"),
        new PortPreset("Minecraft Java", new PortRange(25565, 25565), PortProtocol.Both, "Minecraft Java"),
        new PortPreset("統合版 NetherNet", new PortRange(19132, 19132), PortProtocol.Tcp, "Bedrock シグナリング"),
        new PortPreset("統合版 RakNet", new PortRange(19132, 19133), PortProtocol.Udp, "Bedrock RakNet"),
        new PortPreset("ARK", new PortRange(7777, 7777), PortProtocol.Udp, "ARK"),
        new PortPreset("Valheim", new PortRange(2456, 2458), PortProtocol.Udp, "Valheim"),
        new PortPreset("Terraria", new PortRange(7777, 7777), PortProtocol.Tcp, "Terraria")
    ];
}
