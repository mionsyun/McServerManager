using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using McServerManager.Models.Fabric;

namespace McServerManager.Services.Fabric;

/// <summary>
/// Read-only, bounded Fabric schema-1 metadata inspection. Never loads classes, starts
/// Java, extracts archives, follows entry paths, or treats metadata as proof of safety.
/// All inspection issues block a complete result. Unknown dependency conditions are
/// not silently discarded. Passive custom metadata has no Loader dependency semantics.
/// </summary>
public sealed class FabricJarInspector : IFabricJarInspector
{
    public const int MaxMetadataBytes = 512 * 1024;
    public const int MaxTotalMetadataBytes = 4 * 1024 * 1024;
    public const int MaxNestedDepth = 2;
    public const long MaxNestedBytes = 64L * 1024 * 1024;
    public const long MaxArchiveBytes = FabricZipStructure.MaxArchiveBytes;
    public const int MaxArchiveEntries = FabricZipStructure.MaxEntries;
    public const int MaxCompressionRatio = FabricZipStructure.MaxCompressionRatio;

    public async Task<FabricJarInspection> InspectAsync(Stream archive, string sourceName, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(archive);
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceName);
        cancellationToken.ThrowIfCancellationRequested();
        if (!archive.CanRead || !archive.CanSeek)
            return new() { Issues = [new() { Code = "UnsupportedStream", Message = "Inspection requires a readable, seekable bounded stream.", ArchivePath = sourceName }] };
        var originalPosition = archive.Position;
        try { return await new Session(cancellationToken).RunAsync(archive, sourceName).ConfigureAwait(false); }
        finally { archive.Position = originalPosition; }
    }

    private sealed class Session(CancellationToken ct)
    {
        private readonly List<FabricModMetadata> _mods = [];
        private readonly List<FabricInspectionIssue> _issues = [];
        private readonly IFabricVersionMatcher _matcher = new FabricVersionMatcher();
        private int _entryCount;
        private int _metadataBytes;
        private int _relations;
        private long _nestedBytes;
        private static readonly HashSet<string> KnownFields = new(StringComparer.Ordinal)
        {
            "schemaVersion", "id", "version", "provides", "environment", "entrypoints", "jars", "languageAdapters",
            "mixins", "accessWidener", "name", "description", "authors", "contributors", "contact", "license", "icon", "custom",
            "depends", "recommends", "suggests", "breaks", "conflicts"
        };

        internal async Task<FabricJarInspection> RunAsync(Stream archive, string path)
        {
            var root = await InspectArchiveAsync(archive, path, 0).ConfigureAwait(false);
            foreach (var group in _mods.GroupBy(x => x.Id, StringComparer.Ordinal).Where(x => x.Count() > 1))
            {
                var first = group.First();
                // Repeated exact-content nested JARs are the same candidate, not Loader
                // alternatives. Keep every occurrence and its ancestry in Mods: server
                // applicability is decided per path, and all inspection budgets still apply.
                if (group.All(x => x.NestedDepth > 0 && x.Version == first.Version && x.ContentSha256 == first.ContentSha256))
                    continue;
                _issues.Add(new() { Code = "DuplicateModId", Message = $"Multiple bundled metadata entries declare mod ID {group.Key}; Loader candidate selection is not assumed.", ArchivePath = path });
            }
            return new() { IsComplete = root is not null && _issues.Count == 0, Root = root, Mods = _mods.ToArray(), Issues = _issues.ToArray() };
        }

        private async Task<FabricModMetadata?> InspectArchiveAsync(Stream stream, string path, int depth)
        {
            try
            {
                ct.ThrowIfCancellationRequested();
                if (depth > MaxNestedDepth) FabricZipStructure.Fail("NestedDepthLimit", "Nested JAR depth exceeds two levels.");
                var structure = FabricZipStructure.Validate(stream, ref _entryCount, ct);
                if (!structure.TryGetValue("fabric.mod.json", out var metadata))
                    FabricZipStructure.Fail("MissingFabricMetadata", "Declared Fabric JAR has no root fabric.mod.json.");
                if (metadata!.Size > MaxMetadataBytes || (long)_metadataBytes + metadata.Size > MaxTotalMetadataBytes)
                    FabricZipStructure.Fail("MetadataSizeLimit", "Metadata exceeds the 512 KiB per file or 4 MiB tree limit.");
                stream.Position = 0;
                var sha256 = Convert.ToHexString(await SHA256.HashDataAsync(stream, ct).ConfigureAwait(false)).ToLowerInvariant();
                stream.Position = 0;
                using var archive = new ZipArchive(stream, ZipArchiveMode.Read, leaveOpen: true);
                if (archive.Entries.Count != structure.Count) FabricZipStructure.Fail("InvalidZip", "ZIP readers disagree on entry count.");
                var bytes = await ReadEntryAsync(archive.GetEntry(metadata.Name)!, metadata, MaxMetadataBytes).ConfigureAwait(false);
                _metadataBytes += bytes.Length;
                var mod = ParseMetadata(bytes, path, sha256, depth);
                _mods.Add(mod);
                foreach (var nestedPath in mod.NestedJars)
                {
                    ct.ThrowIfCancellationRequested();
                    if (depth >= MaxNestedDepth) FabricZipStructure.Fail("NestedDepthLimit", "Nested JAR depth exceeds two levels.");
                    if (!structure.TryGetValue(nestedPath, out var nested))
                        FabricZipStructure.Fail("MissingNestedJar", "A declared nested JAR is missing from the archive.");
                    if (_nestedBytes + nested!.Size > MaxNestedBytes)
                        FabricZipStructure.Fail("NestedSizeLimit", "Cumulative declared nested JAR bytes exceed 64 MiB.");
                    _nestedBytes += nested.Size;
                    var nestedBytes = await ReadEntryAsync(archive.GetEntry(nestedPath)!, nested, (int)MaxNestedBytes).ConfigureAwait(false);
                    using var nestedStream = new MemoryStream(nestedBytes, writable: false);
                    await InspectArchiveAsync(nestedStream, path + "!/" + nestedPath, depth + 1).ConfigureAwait(false);
                }
                return mod;
            }
            catch (FabricInspectionException ex) { _issues.Add(new() { Code = ex.Code, Message = ex.Message, ArchivePath = path }); }
            catch (JsonException) { _issues.Add(new() { Code = "InvalidMetadata", Message = "Metadata is invalid JSON or exceeds JSON depth 16.", ArchivePath = path }); }
            catch (InvalidDataException) { _issues.Add(new() { Code = "InvalidZip", Message = "JAR compression or ZIP structure is invalid.", ArchivePath = path }); }
            catch (System.Text.DecoderFallbackException) { _issues.Add(new() { Code = "InvalidZip", Message = "JAR entry names contain invalid UTF-8.", ArchivePath = path }); }
            catch (EndOfStreamException) { _issues.Add(new() { Code = "InvalidZip", Message = "JAR data is truncated.", ArchivePath = path }); }
            return null;
        }

        private async Task<byte[]> ReadEntryAsync(ZipArchiveEntry entry, FabricZipStructure.Entry info, int maximum)
        {
            if (info.Size > maximum || entry.Length != info.Size || entry.CompressedLength != info.CompressedSize)
                FabricZipStructure.Fail("EntrySizeLimit", "ZIP entry size is inconsistent or exceeds its inspection bound.");
            using var input = entry.Open();
            var bytes = new byte[(int)info.Size];
            var offset = 0;
            while (offset < bytes.Length)
            {
                ct.ThrowIfCancellationRequested();
                var read = await input.ReadAsync(bytes.AsMemory(offset, Math.Min(32 * 1024, bytes.Length - offset)), ct).ConfigureAwait(false);
                if (read == 0) FabricZipStructure.Fail("InvalidZip", "ZIP entry ended before its declared size.");
                offset += read;
            }
            var overflow = new byte[1];
            if (await input.ReadAsync(overflow, ct).ConfigureAwait(false) != 0)
                FabricZipStructure.Fail("EntrySizeLimit", "ZIP entry inflated beyond its declared size.");
            if (Crc32(bytes, ct) != info.Crc) FabricZipStructure.Fail("CrcMismatch", "Inspected ZIP entry CRC does not match its directory record.");
            return bytes;
        }

        private FabricModMetadata ParseMetadata(byte[] bytes, string path, string hash, int depth)
        {
            using var document = JsonDocument.Parse(bytes, new JsonDocumentOptions { MaxDepth = 16 });
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object) FabricZipStructure.Fail("UnsupportedSchema", "Fabric metadata must be a schema-1 object.");
            ValidateDuplicates(root);
            if (!root.TryGetProperty("schemaVersion", out var schema) || schema.ValueKind != JsonValueKind.Number || !schema.TryGetInt32(out var version) || version != 1)
                FabricZipStructure.Fail("UnsupportedSchema", "Only Fabric metadata schemaVersion 1 is supported.");
            foreach (var property in root.EnumerateObject())
                if (!KnownFields.Contains(property.Name)) FabricZipStructure.Fail("UnknownMetadata", $"Unknown root metadata field '{property.Name}' requires review.");
            var id = RequiredString(root, "id", 64);
            if (!ValidModId(id)) FabricZipStructure.Fail("InvalidModId", "Metadata declares an invalid Fabric mod ID.");
            var modVersion = RequiredString(root, "version", 256);
            if (modVersion.Any(char.IsWhiteSpace) || modVersion.Any(char.IsControl))
                FabricZipStructure.Fail("UnsupportedVersion", "Whitespace or control characters in a mod version are unsupported.");
            var environment = FabricEnvironment.Universal;
            if (root.TryGetProperty("environment", out var env))
            {
                if (env.ValueKind != JsonValueKind.String) FabricZipStructure.Fail("UnknownEnvironment", "Non-string Fabric environment is unsupported.");
                environment = env.GetString() switch { "*" => FabricEnvironment.Universal, "client" => FabricEnvironment.Client, "server" => FabricEnvironment.Server, _ => FabricEnvironment.Unknown };
                if (environment == FabricEnvironment.Unknown) FabricZipStructure.Fail("UnknownEnvironment", "Unknown Fabric environment requires review.");
            }
            var provides = StringArray(root, "provides", 256);
            if (provides.Any(x => !ValidModId(x) || x == id)) FabricZipStructure.Fail("InvalidProvides", "Fabric provides contains an invalid or self-referential alias.");
            List<string> jars = [];
            if (root.TryGetProperty("jars", out var nested))
            {
                if (nested.ValueKind != JsonValueKind.Array || nested.GetArrayLength() > 256) FabricZipStructure.Fail("InvalidNestedJars", "Malformed or excessive nested JAR declarations.");
                foreach (var item in nested.EnumerateArray())
                {
                    if (item.ValueKind != JsonValueKind.Object || item.EnumerateObject().Any(x => x.Name != "file"))
                        FabricZipStructure.Fail("UnknownNestedCondition", "Unsupported nested JAR metadata or condition.");
                    var file = RequiredString(item, "file", 1024);
                    FabricZipStructure.ValidatePath(file);
                    if (!file.EndsWith(".jar", StringComparison.Ordinal) || jars.Contains(file, StringComparer.OrdinalIgnoreCase))
                        FabricZipStructure.Fail("InvalidNestedJars", "Nested entries must name distinct JAR paths.");
                    jars.Add(file);
                }
            }
            ValidateLoadingShapes(root);
            return new()
            {
                Id = id, Version = modVersion, Environment = environment, ArchivePath = path, ContentSha256 = hash, NestedDepth = depth,
                Provides = provides, NestedJars = jars.ToArray(), Depends = Dependencies(root, "depends"), Recommends = Dependencies(root, "recommends"),
                Suggests = Dependencies(root, "suggests"), Breaks = Dependencies(root, "breaks"), Conflicts = Dependencies(root, "conflicts")
            };
        }

        private IReadOnlyDictionary<string, FabricVersionConstraint> Dependencies(JsonElement root, string name)
        {
            Dictionary<string, FabricVersionConstraint> result = new(StringComparer.Ordinal);
            if (!root.TryGetProperty(name, out var map)) return result;
            if (map.ValueKind != JsonValueKind.Object) FabricZipStructure.Fail("InvalidDependency", $"Fabric {name} must be an object.");
            foreach (var property in map.EnumerateObject())
            {
                ct.ThrowIfCancellationRequested();
                if (++_relations > 4096) FabricZipStructure.Fail("RelationLimit", "Fabric metadata tree exceeds 4096 dependency relations.");
                if (!ValidModId(property.Name)) FabricZipStructure.Fail("InvalidDependency", "A dependency mod ID is invalid.");
                IReadOnlyList<string> alternatives = property.Value.ValueKind switch
                {
                    JsonValueKind.String => [property.Value.GetString()!],
                    JsonValueKind.Array when property.Value.GetArrayLength() is > 0 and <= 32 && property.Value.EnumerateArray().All(x => x.ValueKind == JsonValueKind.String)
                        => property.Value.EnumerateArray().Select(x => x.GetString()!).ToArray(),
                    _ => throw new FabricInspectionException("UnknownDependencyCondition", "Dependency values must be a string or a nonempty bounded OR-array of strings.")
                };
                var constraint = new FabricVersionConstraint { Alternatives = alternatives };
                if (!_matcher.IsSupported(constraint)) FabricZipStructure.Fail("UnknownVersionConstraint", $"Unsupported Fabric version constraint for {property.Name}.");
                result.Add(property.Name, constraint);
            }
            return result;
        }

        private void ValidateDuplicates(JsonElement element)
        {
            ct.ThrowIfCancellationRequested();
            if (element.ValueKind == JsonValueKind.Object)
            {
                HashSet<string> names = new(StringComparer.Ordinal);
                foreach (var item in element.EnumerateObject())
                {
                    if (!names.Add(item.Name)) FabricZipStructure.Fail("DuplicateJsonKey", "Duplicate JSON object keys make Fabric metadata ambiguous.");
                    ValidateDuplicates(item.Value);
                }
            }
            else if (element.ValueKind == JsonValueKind.Array)
                foreach (var item in element.EnumerateArray()) ValidateDuplicates(item);
        }

        private static void ValidateLoadingShapes(JsonElement root)
        {
            foreach (var field in new[] { "entrypoints", "languageAdapters", "custom" })
                if (root.TryGetProperty(field, out var value) && value.ValueKind != JsonValueKind.Object)
                    FabricZipStructure.Fail("InvalidMetadata", $"Fabric {field} must be an object.");
            if (root.TryGetProperty("accessWidener", out var access))
            {
                if (access.ValueKind != JsonValueKind.String) FabricZipStructure.Fail("InvalidMetadata", "accessWidener must be a path string.");
                FabricZipStructure.ValidatePath(access.GetString()!);
            }
            if (!root.TryGetProperty("mixins", out var mixins)) return;
            if (mixins.ValueKind != JsonValueKind.Array) FabricZipStructure.Fail("InvalidMetadata", "mixins must be an array.");
            foreach (var item in mixins.EnumerateArray())
            {
                if (item.ValueKind == JsonValueKind.String) { FabricZipStructure.ValidatePath(item.GetString()!); continue; }
                if (item.ValueKind != JsonValueKind.Object || item.EnumerateObject().Any(x => x.Name is not ("config" or "environment")))
                    FabricZipStructure.Fail("UnknownMetadata", "Unsupported mixin metadata condition.");
                FabricZipStructure.ValidatePath(RequiredString(item, "config", 1024));
                if (item.TryGetProperty("environment", out var environment) && (environment.ValueKind != JsonValueKind.String || environment.GetString() is not ("*" or "client" or "server")))
                    FabricZipStructure.Fail("UnknownEnvironment", "Unknown mixin environment requires review.");
            }
        }

        private static string RequiredString(JsonElement root, string name, int limit)
        {
            if (!root.TryGetProperty(name, out var element) || element.ValueKind != JsonValueKind.String || element.GetString() is not { Length: > 0 } text || text.Length > limit)
                throw new FabricInspectionException("InvalidMetadata", $"Missing, invalid or oversized Fabric {name}.");
            return text;
        }
        private static string[] StringArray(JsonElement root, string name, int maximum)
        {
            if (!root.TryGetProperty(name, out var element)) return [];
            if (element.ValueKind != JsonValueKind.Array || element.GetArrayLength() > maximum || element.EnumerateArray().Any(x => x.ValueKind != JsonValueKind.String))
                throw new FabricInspectionException("InvalidMetadata", $"Malformed or excessive Fabric {name}.");
            var strings = element.EnumerateArray().Select(x => x.GetString()!).ToArray();
            if (strings.Distinct(StringComparer.Ordinal).Count() != strings.Length) FabricZipStructure.Fail("InvalidMetadata", $"Duplicate Fabric {name} values.");
            return strings;
        }
        private static bool ValidModId(string value) => value.Length is >= 2 and <= 64 && value[0] is >= 'a' and <= 'z' && value.All(c => c is >= 'a' and <= 'z' or >= '0' and <= '9' or '-' or '_');
        private static uint Crc32(byte[] bytes, CancellationToken ct)
        {
            var crc = uint.MaxValue;
            for (var i = 0; i < bytes.Length; i++)
            {
                if ((i & 32767) == 0) ct.ThrowIfCancellationRequested();
                crc = CrcTable[(crc ^ bytes[i]) & 255] ^ (crc >> 8);
            }
            return ~crc;
        }
        private static readonly uint[] CrcTable = BuildCrcTable();
        private static uint[] BuildCrcTable()
        {
            var table = new uint[256];
            for (uint i = 0; i < table.Length; i++)
            {
                var value = i;
                for (var bit = 0; bit < 8; bit++) value = (value & 1) != 0 ? 0xedb88320 ^ (value >> 1) : value >> 1;
                table[i] = value;
            }
            return table;
        }
    }
}
