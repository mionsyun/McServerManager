namespace McServerManager.Models;

public sealed class AddonSearchResult
{
    public string ProjectId { get; init; } = string.Empty;
    public string Slug { get; init; } = string.Empty;
    public string Title { get; init; } = string.Empty;
    public string Description { get; init; } = string.Empty;
    public string ProjectType { get; init; } = string.Empty;
    public int DownloadCount { get; init; }
    public string ProjectUrl => string.IsNullOrWhiteSpace(Slug)
        ? string.Empty
        : $"https://modrinth.com/{ProjectType}/{Slug}";
}
