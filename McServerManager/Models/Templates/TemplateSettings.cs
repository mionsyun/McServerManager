namespace McServerManager.Models.Templates;

/// <summary>Only these shared settings may be applied; omission preserves the application's default.</summary>
public sealed record TemplateSettings
{
    public string? Difficulty { get; init; }
    public string? Gamemode { get; init; }
    public int? MaxPlayers { get; init; }
    public int? ViewDistance { get; init; }
    public int? SimulationDistance { get; init; }
    public bool? Pvp { get; init; }
    public int? SpawnProtection { get; init; }
}
