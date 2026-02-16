namespace McServerManager.Models;

public sealed class OpEntry
{
    public string Uuid { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public int Level { get; set; } = 4;
    public bool BypassesPlayerLimit { get; set; }
}
