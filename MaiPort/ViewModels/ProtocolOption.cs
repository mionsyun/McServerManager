using MaiPort.Models;

namespace MaiPort.ViewModels;

/// <summary>
/// プロトコル選択コンボボックスの項目。
/// </summary>
public sealed record ProtocolOption(PortProtocol Value, string Label)
{
    public override string ToString() => Label;
}
