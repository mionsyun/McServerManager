using McServerManager.Models.VanillaRuntime;

namespace McServerManager.Services.VanillaRuntime;

public interface IVanillaRuntimeInspectionService
{
    Task<VanillaRuntimeInspectionResult> InspectAsync(
        string minecraftVersion,
        IProgress<VanillaRuntimeInspectionProgress>? progress = null,
        CancellationToken cancellationToken = default);
}
