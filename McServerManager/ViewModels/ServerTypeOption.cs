namespace McServerManager.ViewModels;

public sealed class ServerTypeOption
{
    public ServerTypeOption(string id, string label)
    {
        Id = id;
        Label = label;
    }

    public string Id { get; }
    public string Label { get; }
}
