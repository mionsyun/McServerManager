namespace McServerManager.Models.Authoring;

/// <summary>
/// Immutable allowlisted registration values. No filesystem access, live-server snapshot, ownership,
/// installed-artifact attestation, or client/addon inference is represented by this object.
/// </summary>
public sealed record RegisteredServerTemplateSource(string ServerId, string ServerType, string MinecraftVersion,
    string Name, string Difficulty, string GameMode, int MaxPlayers, int ViewDistance, bool Pvp,
    int SpawnProtection, ServerCreationRuntimeDeclaration? CreationDeclaration);
