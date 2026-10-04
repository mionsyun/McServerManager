using McServerManager.Models.Templates;

namespace McServerManager.Models.Authoring;

/// <summary>Opaque service-issued identity. Imported server addons are read-only declarations.</summary>
public sealed class TemplateAuthoringSession
{
    internal TemplateAuthoringSession(Guid templateId, int baseRevision, int revision, bool isEditing,
        string? importedManifestSha256, IReadOnlyList<TemplateAddon> preservedServerAddons)
    {
        TemplateId = templateId;
        BaseRevision = baseRevision;
        Revision = revision;
        IsEditing = isEditing;
        ImportedManifestSha256 = importedManifestSha256;
        PreservedServerAddons = preservedServerAddons;
        PreservedServerAddonCount = preservedServerAddons.Count;
        IsServerRuntimeLocked = preservedServerAddons.Count != 0;
    }

    public Guid TemplateId { get; }
    public int BaseRevision { get; }
    public int Revision { get; }
    public bool IsEditing { get; }
    public string? ImportedManifestSha256 { get; }
    public IReadOnlyList<TemplateAddon> PreservedServerAddons { get; }
    public int PreservedServerAddonCount { get; }
    public bool IsServerRuntimeLocked { get; }
}
