using McServerManager.Models.Templates;

namespace McServerManager.Models.Authoring;

public sealed record TemplateAuthoringOpenResult
{
    public required bool IsValid { get; init; }
    public TemplateAuthoringSession? Session { get; init; }
    public TemplateAuthoringDraft? Draft { get; init; }
    public required IReadOnlyList<TemplateValidationIssue> Issues { get; init; }
}

public sealed record TemplateAuthoringValidationResult
{
    public required bool IsValid { get; init; }
    public TemplateAuthoringPreparedExport? PreparedExport { get; init; }
    public required IReadOnlyList<TemplateValidationIssue> Issues { get; init; }
}

/// <summary>Opaque receipt for one immutable, input-validated byte snapshot. Never an installation approval.</summary>
public sealed class TemplateAuthoringPreparedExport
{
    internal TemplateAuthoringPreparedExport(Guid templateId, int revision, string manifestSha256,
        string? clientDefinitionSha256, int byteCount)
    {
        TemplateId = templateId;
        Revision = revision;
        ManifestSha256 = manifestSha256;
        ClientDefinitionSha256 = clientDefinitionSha256;
        ByteCount = byteCount;
    }

    public Guid TemplateId { get; }
    public int Revision { get; }
    public string ManifestSha256 { get; }
    public string? ClientDefinitionSha256 { get; }
    public int ByteCount { get; }
    public bool InputValidatedOnly { get; } = true;
}
