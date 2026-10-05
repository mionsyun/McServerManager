using McServerManager.Models.Modrinth;
using McServerManager.Services.Modrinth;

namespace McServerManager.Tests.TestInfrastructure;

internal sealed class StubModInspectionService : IModrinthInspectionService
{
    public Task<ModrinthInspectionResult> InspectAsync(ModrinthInspectionRequest request, CancellationToken cancellationToken = default)
        => Task.FromResult(new ModrinthInspectionResult());
}
