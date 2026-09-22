using MaiPort.Models;
using MaiPort.Utilities;

namespace MaiPort.ViewModels;

/// <summary>
/// server.properties から判定した「開放すべきポート」1件の表示用。
/// </summary>
public sealed class BedrockPlanItemViewModel
{
    public BedrockPlanItemViewModel(BedrockPortPlanItem item)
    {
        Item = item;
    }

    public BedrockPortPlanItem Item { get; }

    public string Label => $"{BedrockSetupViewModel.DescribeProtocol(Item.Protocol)} {PortRangeParser.Format(Item.Range)}";

    public string Purpose => Item.Purpose;
}
