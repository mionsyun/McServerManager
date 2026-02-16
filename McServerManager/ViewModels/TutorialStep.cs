namespace McServerManager.ViewModels;

public enum TutorialPlacement
{
    Center,
    Top,
    Bottom,
    Left,
    Right
}

public sealed class TutorialStep
{
    public TutorialStep(string title, string body, string? targetName, TutorialPlacement placement, string? preferredTabName = null)
    {
        Title = title;
        Body = body;
        TargetName = targetName;
        Placement = placement;
        PreferredTabName = preferredTabName;
    }

    public string Title { get; }
    public string Body { get; }
    public string? TargetName { get; }
    public TutorialPlacement Placement { get; }
    public string? PreferredTabName { get; }
}
