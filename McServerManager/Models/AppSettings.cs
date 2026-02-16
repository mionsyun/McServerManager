namespace McServerManager.Models;

public sealed class AppSettings
{
    public bool HasShownFirstRun { get; set; }
    public bool HasCompletedTutorial { get; set; }
    public List<string> ServerDirectories { get; set; } = new();
    public bool EnableUpnp { get; set; }
    public bool PromptUpnp { get; set; } = true;
    public string Theme { get; set; } = "Dark";
}
