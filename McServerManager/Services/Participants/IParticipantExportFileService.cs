using System.Threading;

namespace McServerManager.Services.Participants;

/// <summary>Filesystem boundary for explicitly selected participant-definition and ZIP paths.</summary>
public interface IParticipantExportFileService
{
    /// <summary>Reads at most the allowed definition size; parsing belongs to the definition service.</summary>
    Task<byte[]> ReadDefinitionAsync(string path, CancellationToken cancellationToken = default);

    /// <summary>Reads an explicitly selected template, bounded to the template manifest limit.</summary>
    Task<byte[]> ReadTemplateAsync(string path, CancellationToken cancellationToken = default);

    /// <summary>
    /// Publishes a complete, prebuilt archive to a new .zip file and returns its normalized path.
    /// Existing files are never replaced. Successful publication is not subsequently cancelled.
    /// </summary>
    Task<string> SaveNewZipAsync(string path, ReadOnlyMemory<byte> bytes, CancellationToken cancellationToken = default);
}
