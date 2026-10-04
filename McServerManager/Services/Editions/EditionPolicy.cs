using McServerManager.Models.Editions;

namespace McServerManager.Services.Editions;

/// <summary>
/// Immutable feature boundary for a distribution. This is product packaging, not license verification.
/// User settings, imported templates and purchase links cannot change the installed edition.
/// </summary>
public sealed class EditionPolicy : IEditionPolicy
{
#if MAIPILOT_PRO
    public static EditionPolicy Current { get; } = new(AppEdition.Pro);
#else
    public static EditionPolicy Current { get; } = new(AppEdition.Free);
#endif

    public EditionPolicy(AppEdition edition)
    {
        if (!Enum.IsDefined(edition)) throw new ArgumentOutOfRangeException(nameof(edition));
        Edition = edition;
    }

    public AppEdition Edition { get; }
    public string DisplayName => Edition == AppEdition.Pro ? "MaiPilot Pro" : "MaiPilot Free";

    public bool Allows(EditionCapability capability) => capability switch
    {
        EditionCapability.TemplateUse or EditionCapability.TemplateImport or
            EditionCapability.DependencyCheck or EditionCapability.ParticipantPackExport => true,
        EditionCapability.TemplateCreate or EditionCapability.TemplateEdit or
            EditionCapability.TemplateExport => Edition == AppEdition.Pro,
        // Runtime safety is independent of product edition. Neither distribution can apply templates yet.
        EditionCapability.ProductionTemplateApply => false,
        _ => false
    };
}
