namespace McServerManager.Models;

public sealed class ArchiveWorldCandidate
{
    public ArchiveWorldCandidate(string relativePath, string displayName)
    {
        RelativePath = relativePath ?? string.Empty;
        DisplayName = string.IsNullOrWhiteSpace(displayName) ? "(ZIP root)" : displayName;
    }

    public string RelativePath { get; }

    public string DisplayName { get; }
}
