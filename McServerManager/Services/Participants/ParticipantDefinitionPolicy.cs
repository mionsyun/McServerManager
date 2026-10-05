namespace McServerManager.Services.Participants;

/// <summary>Application-owned bounds; a client definition cannot change them.</summary>
public static class ParticipantDefinitionPolicy
{
    public const int MaxDefinitionBytes = 256 * 1024;
    public const int MaxJsonDepth = 8;
    public const int MaxMods = 256;
    public const int MaxNameLength = 120;
    public const int MaxVersionLength = 96;
    public const int MaxNoteLength = 240;
    public const int MaxZipBytes = 2 * 1024 * 1024;
}
