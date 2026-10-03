using McServerManager.Models.Fabric;

namespace McServerManager.Services.Fabric;

public interface IFabricVersionMatcher
{
    FabricVersionMatch Match(string candidateVersion, FabricVersionConstraint constraint);
    bool IsSupported(FabricVersionConstraint constraint);
}
