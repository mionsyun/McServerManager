using System.Threading;
using McServerManager.Models.Participants;

namespace McServerManager.Services.Participants;

public interface IParticipantListZipService
{
    /// <summary>Produces a complete archive in memory, or throws without publishing partial bytes.</summary>
    Task<byte[]> CreateAsync(ParticipantClientDefinition definition, CancellationToken cancellationToken = default);
}
