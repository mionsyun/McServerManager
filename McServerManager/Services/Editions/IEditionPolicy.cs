using McServerManager.Models.Editions;

namespace McServerManager.Services.Editions;

public interface IEditionPolicy
{
    AppEdition Edition { get; }
    string DisplayName { get; }
    bool Allows(EditionCapability capability);
}
