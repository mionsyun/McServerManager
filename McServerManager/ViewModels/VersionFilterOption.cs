namespace McServerManager.ViewModels;

public sealed class VersionFilterOption
{
    public VersionFilterOption(string id, string label)
    {
        Id = id;
        Label = label;
    }

    public string Id { get; }
    public string Label { get; }
}
