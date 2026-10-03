namespace McServerManager.Models.Templates;

public sealed record TemplateValidationIssue(string Code, string Path, string Message);

public sealed record TemplateValidationResult
{
    public required bool IsValid { get; init; }
    public TemplateManifest? Manifest { get; init; }
    public string? ManifestSha256 { get; init; }
    public required IReadOnlyList<TemplateValidationIssue> Issues { get; init; }
}
