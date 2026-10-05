using System.Collections.ObjectModel;

namespace McServerManager.Models.Participants;

/// <summary>
/// An explicit client list, created only by the bounded offline definition parser.
/// Input validation does not establish provider identity, completeness or compatibility.
/// </summary>
public sealed class ParticipantClientDefinition
{
    internal ParticipantClientDefinition(string name, ParticipantClientRuntime runtime,
        ReadOnlyCollection<ParticipantClientMod> mods, string inputSha256)
    {
        Name = name;
        Runtime = runtime;
        Mods = mods;
        InputSha256 = inputSha256;
    }

    public string Name { get; }
    public ParticipantClientRuntime Runtime { get; }
    public ReadOnlyCollection<ParticipantClientMod> Mods { get; }
    public string InputSha256 { get; }
    public ParticipantDefinitionReadiness Readiness { get; } = ParticipantDefinitionReadiness.InputValidatedOnly;
}

public enum ParticipantDefinitionReadiness { InputValidatedOnly }

public sealed class ParticipantClientRuntime
{
    internal ParticipantClientRuntime(string minecraftVersion, string loader, string loaderVersion, string javaVersion)
    {
        MinecraftVersion = minecraftVersion;
        Loader = loader;
        LoaderVersion = loaderVersion;
        JavaVersion = javaVersion;
    }

    public string MinecraftVersion { get; }
    public string Loader { get; }
    public string LoaderVersion { get; }
    public string JavaVersion { get; }
}

public sealed class ParticipantClientMod
{
    internal ParticipantClientMod(string projectId, string versionId, string name, string version,
        string clientSide, string? note)
    {
        ProjectId = projectId;
        VersionId = versionId;
        Name = name;
        Version = version;
        ClientSide = clientSide;
        Note = note;
    }

    public string Provider { get; } = "modrinth";
    public string ProjectId { get; }
    public string VersionId { get; }
    public string Name { get; }
    public string Version { get; }
    public string ClientSide { get; }
    public string? Note { get; }
}
