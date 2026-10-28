using System.Text.RegularExpressions;
using McServerManager.Models.Authoring;
using McServerManager.Services.Templates;

namespace McServerManager.Services.Authoring;

/// <summary>Validates a historical creation declaration, not the current installed runtime.</summary>
public static class ServerCreationRuntimeDeclarationPolicy
{
    public const int CurrentSchemaVersion = 1;

    // Keep the fixed-release syntax and bound aligned with TemplateManifestReader.
    private static readonly Regex MinecraftReleasePattern = new(
        "\\A[0-9]+\\.[0-9]+(?:\\.[0-9]+)?\\z",
        RegexOptions.CultureInvariant | RegexOptions.NonBacktracking);

    public static ServerCreationRuntimeDeclaration? Create(string? serverId, string? serverType, string? version)
    {
        if (!IsSupportedSelection(serverId, serverType, version))
            return null;

        return new(CurrentSchemaVersion, serverId!, serverType!, version!);
    }

    public static bool IsMatching(ServerCreationRuntimeDeclaration? declaration,
        string? serverId, string? serverType, string? version) =>
        declaration is not null &&
        declaration.SchemaVersion == CurrentSchemaVersion &&
        IsSupportedSelection(declaration.ServerId, declaration.ServerType, declaration.MinecraftVersion) &&
        string.Equals(declaration.ServerId, serverId, StringComparison.Ordinal) &&
        string.Equals(declaration.ServerType, serverType, StringComparison.Ordinal) &&
        string.Equals(declaration.MinecraftVersion, version, StringComparison.Ordinal);

    private static bool IsSupportedSelection(string? serverId, string? serverType, string? version) =>
        serverId is { Length: 32 } &&
        Guid.TryParseExact(serverId, "N", out var parsedId) &&
        parsedId != Guid.Empty &&
        string.Equals(parsedId.ToString("N"), serverId, StringComparison.Ordinal) &&
        string.Equals(serverType, "Vanilla", StringComparison.Ordinal) &&
        version is { Length: > 0 and <= TemplatePolicy.MaxRuntimeVersionLength } &&
        MinecraftReleasePattern.IsMatch(version);
}
