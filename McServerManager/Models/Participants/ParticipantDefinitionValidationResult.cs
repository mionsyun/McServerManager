namespace McServerManager.Models.Participants;

public sealed record ParticipantDefinitionIssue(string Code, string Path, string Message);

public sealed record ParticipantDefinitionValidationResult(
    ParticipantClientDefinition? Definition,
    IReadOnlyList<ParticipantDefinitionIssue> Issues)
{
    public bool IsValid => Definition is not null && Issues.Count == 0;
}
