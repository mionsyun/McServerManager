using McServerManager.Models.Authoring;

namespace McServerManager.Services.Authoring;

public interface ITemplateAuthoringService
{
    TemplateAuthoringOpenResult CreateNew(CancellationToken cancellationToken = default);
    /// <summary>Seeds a new declaration from explicit creation-time metadata and registered settings; not server capture.</summary>
    TemplateAuthoringOpenResult CreateFromRegisteredSettings(RegisteredServerTemplateSource source,
        CancellationToken cancellationToken = default);
    TemplateAuthoringOpenResult OpenForEdit(ReadOnlyMemory<byte> utf8Json, CancellationToken cancellationToken = default);
    TemplateAuthoringValidationResult PrepareExport(TemplateAuthoringSession session, TemplateAuthoringDraft draft,
        CancellationToken cancellationToken = default);
    /// <summary>Rechecks edition, session and current draft identity, then returns a defensive byte copy.</summary>
    byte[] Export(TemplateAuthoringSession session, TemplateAuthoringDraft currentDraft,
        TemplateAuthoringPreparedExport preparedExport, CancellationToken cancellationToken = default);
}
