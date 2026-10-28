using System.Threading;

namespace McServerManager.Services.AuthoringFiles;

public interface ITemplateAuthoringFileService
{
    Task<byte[]> ReadTemplateAsync(string path, CancellationToken cancellationToken = default);
    Task<string> SaveNewTemplateAsync(string path, ReadOnlyMemory<byte> bytes,
        CancellationToken cancellationToken = default);
}
