using System.Text;
using System.Text.Json;
using McServerManager.Models.Authoring;
using McServerManager.Models.Templates;
using McServerManager.Services.Participants;
using McServerManager.Services.Templates;

namespace McServerManager.Services.Authoring;

/// <summary>Writes only the strict declaration schema, retaining every permitted field without inference.</summary>
internal static class TemplateAuthoringSerializer
{
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);

    public static byte[] Serialize(TemplateAuthoringSession session, TemplateAuthoringDraft draft,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var client = draft.ClientDefinition is { } definition ? SerializeClient(definition, cancellationToken) : null;
        using var output = new BoundedOutput(TemplatePolicy.MaxManifestBytes, "ManifestTooLarge", "$",
            "テンプレートのサイズ上限 1 MiB を超えています。");
        using (var writer = new Utf8JsonWriter(output))
        {
            writer.WriteStartObject();
            Write(writer, "schemaVersion", "1.0");
            writer.WriteString("templateId", session.TemplateId);
            writer.WriteNumber("revision", session.Revision);
            Write(writer, "name", draft.Name);
            Write(writer, "description", draft.Description);
            Write(writer, "edition", "java");
            writer.WritePropertyName("runtime");
            WriteRuntime(writer, draft.Runtime);
            writer.WritePropertyName("settings");
            WriteSettings(writer, draft.Settings);
            writer.WriteStartArray("addons");
            foreach (var addon in session.PreservedServerAddons)
            {
                cancellationToken.ThrowIfCancellationRequested();
                WriteAddon(writer, addon);
            }
            writer.WriteEndArray();
            if (client is not null)
            {
                writer.WritePropertyName("clientDefinition");
                writer.WriteRawValue(client, skipInputValidation: false);
            }
            writer.WriteEndObject();
        }
        cancellationToken.ThrowIfCancellationRequested();
        return output.ToArray();
    }

    private static byte[] SerializeClient(TemplateAuthoringClientDraft client, CancellationToken cancellationToken)
    {
        if (client.Mods is { Count: > ParticipantDefinitionPolicy.MaxMods })
            throw new TemplateAuthoringSerializationException("TooManyMods", "$.clientDefinition.mods", "MOD の件数上限を超えています。");
        // Lists belong to the caller. Snapshot once so validation and hashing see the same rows.
        var mods = client.Mods?.Take(ParticipantDefinitionPolicy.MaxMods + 1).ToArray();
        if (mods is { Length: > ParticipantDefinitionPolicy.MaxMods })
            throw new TemplateAuthoringSerializationException("TooManyMods", "$.clientDefinition.mods", "MOD の件数上限を超えています。");
        using var output = new BoundedOutput(ParticipantDefinitionPolicy.MaxDefinitionBytes, "DefinitionTooLarge",
            "$.clientDefinition", "クライアント定義のサイズ上限 256 KiB を超えています。");
        using (var writer = new Utf8JsonWriter(output))
        {
            writer.WriteStartObject();
            Write(writer, "schemaVersion", "1.0");
            Write(writer, "definitionKind", "client");
            Write(writer, "name", client.Name);
            writer.WriteStartObject("runtime");
            Write(writer, "minecraftVersion", client.MinecraftVersion);
            Write(writer, "loader", client.Loader);
            Write(writer, "loaderVersion", client.LoaderVersion);
            Write(writer, "javaVersion", client.JavaVersion);
            writer.WriteEndObject();
            writer.WritePropertyName("mods");
            if (mods is null) writer.WriteNullValue();
            else
            {
                writer.WriteStartArray();
                foreach (var mod in mods)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (mod is null) { writer.WriteNullValue(); continue; }
                    writer.WriteStartObject();
                    Write(writer, "provider", mod.Provider);
                    Write(writer, "projectId", mod.ProjectId);
                    Write(writer, "versionId", mod.VersionId);
                    Write(writer, "name", mod.Name);
                    Write(writer, "version", mod.Version);
                    Write(writer, "clientSide", mod.ClientSide);
                    Optional(writer, "note", mod.Note);
                    writer.WriteEndObject();
                }
                writer.WriteEndArray();
            }
            writer.WriteEndObject();
        }
        return output.ToArray();
    }

    private static void WriteRuntime(Utf8JsonWriter writer, TemplateRuntime? runtime)
    {
        if (runtime is null) { writer.WriteNullValue(); return; }
        writer.WriteStartObject();
        Write(writer, "type", runtime.Type);
        Write(writer, "minecraftVersion", runtime.MinecraftVersion);
        Optional(writer, "build", runtime.Build);
        Optional(writer, "loaderVersion", runtime.LoaderVersion);
        Optional(writer, "installerVersion", runtime.InstallerVersion);
        writer.WriteEndObject();
    }

    private static void WriteSettings(Utf8JsonWriter writer, TemplateSettings? settings)
    {
        if (settings is null) { writer.WriteNullValue(); return; }
        writer.WriteStartObject();
        Optional(writer, "difficulty", settings.Difficulty);
        Optional(writer, "gamemode", settings.Gamemode);
        if (settings.MaxPlayers is { } maxPlayers) writer.WriteNumber("maxPlayers", maxPlayers);
        if (settings.ViewDistance is { } viewDistance) writer.WriteNumber("viewDistance", viewDistance);
        if (settings.SimulationDistance is { } simulationDistance) writer.WriteNumber("simulationDistance", simulationDistance);
        if (settings.Pvp is { } pvp) writer.WriteBoolean("pvp", pvp);
        if (settings.SpawnProtection is { } spawnProtection) writer.WriteNumber("spawnProtection", spawnProtection);
        writer.WriteEndObject();
    }

    private static void WriteAddon(Utf8JsonWriter writer, TemplateAddon addon)
    {
        writer.WriteStartObject();
        Write(writer, "entryId", addon.EntryId);
        Write(writer, "kind", addon.Kind);
        Write(writer, "selection", addon.Selection);
        writer.WriteStartObject("source");
        Write(writer, "provider", addon.Source.Provider);
        Write(writer, "projectId", addon.Source.ProjectId);
        Optional(writer, "versionId", addon.Source.VersionId);
        Optional(writer, "fileId", addon.Source.FileId);
        Write(writer, "fileName", addon.Source.FileName);
        Optional(writer, "sha512", addon.Source.Sha512);
        Optional(writer, "sha1", addon.Source.Sha1);
        writer.WriteEndObject();
        Write(writer, "sha256", addon.Sha256);
        writer.WriteNumber("sizeBytes", addon.SizeBytes);
        writer.WriteStartArray("requires");
        foreach (var dependency in addon.Requires) writer.WriteStringValue(dependency);
        writer.WriteEndArray();
        writer.WriteEndObject();
    }

    private static void Optional(Utf8JsonWriter writer, string name, string? value)
    {
        if (value is not null) Write(writer, name, value);
    }

    private static void Write(Utf8JsonWriter writer, string name, string? value)
    {
        if (value is not null)
        {
            if (value.Length > TemplatePolicy.MaxManifestBytes)
                throw new TemplateAuthoringSerializationException("ManifestTooLarge", "$", "テンプレートのサイズ上限 1 MiB を超えています。");
            // Utf8JsonWriter otherwise replaces unpaired UTF-16 surrogates, silently changing input.
            try { StrictUtf8.GetByteCount(value); }
            catch (EncoderFallbackException)
            {
                throw new TemplateAuthoringSerializationException("InvalidUnicode", "$", "不正な Unicode 文字が含まれています。");
            }
        }
        writer.WriteString(name, value);
    }

    private sealed class BoundedOutput(int maximum, string code, string path, string message) : MemoryStream
    {
        public override void Write(byte[] buffer, int offset, int count)
        {
            Check(count);
            base.Write(buffer, offset, count);
        }

        public override void Write(ReadOnlySpan<byte> buffer)
        {
            Check(buffer.Length);
            base.Write(buffer);
        }

        private void Check(int length)
        {
            if (length > maximum - Length)
                throw new TemplateAuthoringSerializationException(code, path, message);
        }
    }
}

internal sealed class TemplateAuthoringSerializationException(string code, string path, string message) : Exception(message)
{
    public string Code { get; } = code;
    public string Path { get; } = path;
}
