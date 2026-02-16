namespace McServerManager.ViewModels;

public sealed class LaunchModeOption
{
    public LaunchModeOption(string id, string label)
    {
        Id = id;
        Label = label;
    }

    public string Id { get; }
    public string Label { get; }
}
