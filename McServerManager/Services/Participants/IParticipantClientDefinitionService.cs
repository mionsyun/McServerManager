using System.Threading;
using McServerManager.Models.Participants;

namespace McServerManager.Services.Participants;

public interface IParticipantClientDefinitionService
{
    ParticipantDefinitionValidationResult Parse(ReadOnlyMemory<byte> utf8Json,
        CancellationToken cancellationToken = default);
    Task<ParticipantDefinitionValidationResult> ReadAsync(Stream input,
        CancellationToken cancellationToken = default);
}
