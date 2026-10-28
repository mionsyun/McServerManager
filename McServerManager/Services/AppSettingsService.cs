using System.IO;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using McServerManager.Models;

namespace McServerManager.Services;

public sealed class AppSettingsService : IAppSettingsService
{
    private const int SupportedSchemaVersion = 1;
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true, MaxDepth = 32 };
    private static readonly HashSet<string> KnownPropertyNames = JsonSerializer.SerializeToElement(new AppSettings(), Options)
        .EnumerateObject().Select(property => property.Name).Append("SchemaVersion").ToHashSet(StringComparer.Ordinal);
    private static readonly HashSet<string> KnownPropertyNamesIgnoreCase = new(KnownPropertyNames, StringComparer.OrdinalIgnoreCase);
    private readonly AppSettingsFileStore _store;
    private readonly ConditionalWeakTable<AppSettings, Snapshot> _snapshots = new();

    public AppSettingsService(AppPathsService pathsService)
    {
        _store = new AppSettingsFileStore(pathsService.AppSettingsPath);
    }

    internal AppSettingsService(AppSettingsFileStore store) => _store = store;

    public AppSettings Load()
    {
        using var lease = _store.AcquireLock();
        return Read().Settings;
    }

    public void Save(AppSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        using var lease = _store.AcquireLock();
        Save(settings, Read());
    }

    /// <summary>Load, apply and commit under the same cross-process lock.</summary>
    public AppSettings Update(Action<AppSettings> apply)
    {
        ArgumentNullException.ThrowIfNull(apply);
        using var lease = _store.AcquireLock();
        var current = Read();
        apply(current.Settings);
        // Also detect older builds or external editors that do not honor the lock and
        // changed the file while the callback was running. They must still be closed
        // before editing: no cooperative lock can protect against an uncooperative writer.
        Save(current.Settings, Read());
        return current.Settings;
    }

    private Snapshot Read()
    {
        var bytes = _store.Read();
        var settings = new AppSettings();
        JsonElement? original = null;
        if (bytes is not null)
        {
            try
            {
                _ = StrictUtf8.GetCharCount(bytes);
                // Old versions used ReadAllText, which accepted a UTF-8 BOM.
                var json = bytes.AsMemory();
                if (bytes.AsSpan().StartsWith(new byte[] { 0xef, 0xbb, 0xbf })) json = json[3..];
                using var document = JsonDocument.Parse(json, new JsonDocumentOptions { MaxDepth = 32 });
                var root = document.RootElement;
                if (root.ValueKind != JsonValueKind.Object)
                    throw new AppSettingsStorageException(AppSettingsStorageError.InvalidData);
                ValidateUnicode(root);
                var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (var property in root.EnumerateObject())
                {
                    if (!names.Add(property.Name) ||
                        (KnownPropertyNamesIgnoreCase.Contains(property.Name) && !KnownPropertyNames.Contains(property.Name)))
                        throw new AppSettingsStorageException(AppSettingsStorageError.InvalidData);
                }
                if (root.TryGetProperty("SchemaVersion", out var schemaVersion) &&
                    (schemaVersion.ValueKind != JsonValueKind.Number || !schemaVersion.TryGetInt32(out var version) ||
                     version != SupportedSchemaVersion))
                    throw new AppSettingsStorageException(AppSettingsStorageError.UnsupportedVersion);
                settings = root.Deserialize<AppSettings>(Options)
                    ?? throw new AppSettingsStorageException(AppSettingsStorageError.InvalidData);
                Validate(settings);
                original = root.Clone();
            }
            catch (JsonException ex)
            {
                throw new AppSettingsStorageException(AppSettingsStorageError.InvalidData, ex);
            }
            catch (DecoderFallbackException ex)
            {
                throw new AppSettingsStorageException(AppSettingsStorageError.InvalidData, ex);
            }
            catch (Exception ex) when (ex is InvalidOperationException or EncoderFallbackException)
            {
                throw new AppSettingsStorageException(AppSettingsStorageError.InvalidData, ex);
            }
        }
        var snapshot = new Snapshot(settings, bytes is null ? null : SHA256.HashData(bytes), original);
        _snapshots.Add(settings, snapshot);
        return snapshot;
    }

    private void Save(AppSettings settings, Snapshot current)
    {
        if (_snapshots.TryGetValue(settings, out var loaded))
        {
            if (!SameRevision(loaded.Revision, current.Revision))
                throw new AppSettingsStorageException(AppSettingsStorageError.Conflict);
        }
        else if (current.Revision is not null)
        {
            // An untracked/default object is never allowed to replace existing settings.
            throw new AppSettingsStorageException(AppSettingsStorageError.Conflict);
        }

        Validate(settings);
        var bytes = Serialize(settings, current.Original);
        _store.Write(bytes, replaceExisting: current.Revision is not null);
        _snapshots.Remove(settings);
        _snapshots.Add(settings, new Snapshot(settings, SHA256.HashData(bytes), current.Original));
    }

    private static byte[] Serialize(AppSettings settings, JsonElement? original)
    {
        try
        {
            var known = JsonSerializer.SerializeToElement(settings, Options);
            using var buffer = new MemoryStream();
            using (var writer = new Utf8JsonWriter(buffer, new JsonWriterOptions { Indented = true, MaxDepth = 32 }))
            {
                writer.WriteStartObject();
                foreach (var property in known.EnumerateObject()) property.WriteTo(writer);
                if (original is { } existing)
                {
                    foreach (var property in existing.EnumerateObject())
                        if (!known.TryGetProperty(property.Name, out _)) property.WriteTo(writer);
                }
                writer.WriteEndObject();
            }
            if (buffer.Length > AppSettingsFileStore.MaxFileBytes)
                throw new AppSettingsStorageException(AppSettingsStorageError.TooLarge);
            return buffer.ToArray();
        }
        catch (JsonException ex)
        {
            throw new AppSettingsStorageException(AppSettingsStorageError.InvalidData, ex);
        }
    }

    private static void Validate(AppSettings settings)
    {
        if (settings.Theme is null || settings.ServerDirectories is null || settings.ServerDirectories.Any(path => path is null))
            throw new AppSettingsStorageException(AppSettingsStorageError.InvalidData);
    }

    private static void ValidateUnicode(JsonElement element)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                foreach (var property in element.EnumerateObject())
                {
                    _ = StrictUtf8.GetByteCount(property.Name);
                    ValidateUnicode(property.Value);
                }
                break;
            case JsonValueKind.Array:
                foreach (var item in element.EnumerateArray()) ValidateUnicode(item);
                break;
            case JsonValueKind.String:
                _ = StrictUtf8.GetByteCount(element.GetString()!);
                break;
        }
    }

    private static bool SameRevision(byte[]? left, byte[]? right) =>
        left is null ? right is null : right is not null && left.AsSpan().SequenceEqual(right);

    private sealed record Snapshot(AppSettings Settings, byte[]? Revision, JsonElement? Original);
}
