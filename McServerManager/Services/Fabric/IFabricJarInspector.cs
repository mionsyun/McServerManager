using McServerManager.Models.Fabric;

namespace McServerManager.Services.Fabric;

public interface IFabricJarInspector
{
    /// <summary>Inspects a readable, seekable ZIP/JAR without executing or extracting it. Leaves the stream open.</summary>
    Task<FabricJarInspection> InspectAsync(Stream archive, string sourceName, CancellationToken cancellationToken = default);
}
