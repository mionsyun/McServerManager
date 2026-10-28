namespace McServerManager.Models.Authoring;

/// <summary>
/// Historical runtime selection recorded after normal server provisioning. This is not
/// proof of installed files, hashes, native inspection, or authority to apply a template.
/// </summary>
public sealed record ServerCreationRuntimeDeclaration(
    int SchemaVersion,
    string ServerId,
    string ServerType,
    string MinecraftVersion);
