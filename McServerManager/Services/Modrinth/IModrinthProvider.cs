using McServerManager.Models.Modrinth;

namespace McServerManager.Services.Modrinth;

public interface IModrinthProvider
{
    Task<ModrinthVersion> GetVersionAsync(string versionId, CancellationToken cancellationToken = default);
    Task<ModrinthVersion?> GetVersionByHashAsync(string sha512, CancellationToken cancellationToken = default);
    Task<ModrinthProject> GetProjectAsync(string projectId, CancellationToken cancellationToken = default);
    Task<ModrinthDownload> DownloadAsync(ModrinthVersion version, ModrinthFile file, CancellationToken cancellationToken = default);
}
public interface IModrinthInspectionService
{
    Task<ModrinthInspectionResult> InspectAsync(ModrinthInspectionRequest request, CancellationToken cancellationToken = default);
}
