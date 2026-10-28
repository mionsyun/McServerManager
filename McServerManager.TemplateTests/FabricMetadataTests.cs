using System.Buffers.Binary;
using System.IO.Compression;
using System.Text;
using System.Text.Json;
using McServerManager.Models.Fabric;
using McServerManager.Services.Fabric;

namespace McServerManager.TemplateTests;

public sealed class FabricMetadataTests
{
    private const string Minimal = "{\"schemaVersion\":1,\"id\":\"example-mod\",\"version\":\"1.2.3\"}";
    private readonly FabricJarInspector _inspector = new();

    [Fact]
    public async Task ReadsAllRelationsAliasesAndEnvironmentWithoutExecutingPayloads()
    {
        var json = """{"schemaVersion":1,"id":"example-mod","version":"1.2.3+build","environment":"server","provides":["example-alias"],"depends":{"minecraft":[">=1.21 <1.22","1.20.6"],"java":">=21"},"recommends":{"optional-mod":"*"},"suggests":{"extra-mod":"~1"},"breaks":{"bad-mod":"1.x"},"conflicts":{"warning-mod":"^0.2"},"entrypoints":{"main":["evil.NeverExecute"]},"custom":{"passive":{"some":"data"}}}""";
        using var stream = new MemoryStream(Jar(("fabric.mod.json", Utf8(json)), ("evil/NeverExecute.class", Utf8("not even a class"))));
        stream.Position = 7;
        var result = await _inspector.InspectAsync(stream, "test.jar");
        Assert.True(result.IsComplete, JsonSerializer.Serialize(result.Issues));
        Assert.Equal(7, stream.Position);
        Assert.True(stream.CanRead);
        var root = Assert.Single(result.Mods);
        Assert.Same(result.Root, root);
        Assert.Equal("example-mod", root.Id);
        Assert.Equal("1.2.3+build", root.Version);
        Assert.Equal(FabricEnvironment.Server, root.Environment);
        Assert.Equal("example-alias", Assert.Single(root.Provides));
        Assert.Equal(2, root.Depends["minecraft"].Alternatives.Count);
        Assert.Single(root.Recommends);
        Assert.Single(root.Suggests);
        Assert.Single(root.Breaks);
        Assert.Single(root.Conflicts);
        Assert.Matches("^[a-f0-9]{64}$", root.ContentSha256);
    }

    [Fact]
    public async Task InspectsDeclaredNestedJarsAndPreservesClientSideMetadata()
    {
        var nested = Jar(("fabric.mod.json", Utf8("""{"schemaVersion":1,"id":"client-helper","version":"1","environment":"client"}""")));
        var root = Minimal[..^1] + ",\"jars\":[{\"file\":\"META-INF/jars/helper.jar\"}]}";
        var result = await Inspect(Jar(("fabric.mod.json", Utf8(root)), ("META-INF/jars/helper.jar", nested), ("ignored.jar", Utf8("unlisted data"))));
        Assert.True(result.IsComplete, JsonSerializer.Serialize(result.Issues));
        Assert.Equal(2, result.Mods.Count);
        Assert.Equal(FabricEnvironment.Client, result.Mods[1].Environment);
        Assert.Equal(1, result.Mods[1].NestedDepth);
        Assert.Equal("test.jar!/META-INF/jars/helper.jar", result.Mods[1].ArchivePath);
        Assert.NotEqual(result.Mods[0].ContentSha256, result.Mods[1].ContentSha256);
    }

    [Fact]
    public async Task UppercaseNestedJarSuffixCannotSupplyAProviderLoaderWouldIgnore()
    {
        var root = Minimal[..^1] + ",\"depends\":{\"needed-mod\":\"*\"},\"jars\":[{\"file\":\"helper.JAR\"}]}";
        var nested = Jar(("fabric.mod.json", Utf8("{\"schemaVersion\":1,\"id\":\"needed-mod\",\"version\":\"1\"}")));
        var result = await Inspect(Jar(("fabric.mod.json", Utf8(root)), ("helper.JAR", nested)));
        Assert.False(result.IsComplete);
        Assert.Contains(result.Issues, x => x.Code == "InvalidNestedJars");
        Assert.DoesNotContain(result.Mods, x => x.Id == "needed-mod");
    }

    [Fact]
    public async Task TwoNestedLevelsAllowedButThirdBlocks()
    {
        var depth2 = Wrap(Wrap(Jar(("fabric.mod.json", Utf8(Minimal))), "middle-mod"), "outer-mod");
        Assert.True((await Inspect(depth2)).IsComplete);
        var depth3 = Wrap(depth2, "top-mod");
        Assert.Contains((await Inspect(depth3)).Issues, x => x.Code == "NestedDepthLimit");
    }

    [Theory]
    [InlineData("{\"schemaVersion\":2,\"id\":\"example-mod\",\"version\":\"1\"}", "UnsupportedSchema")]
    [InlineData("{\"schemaVersion\":\"1\",\"id\":\"example-mod\",\"version\":\"1\"}", "UnsupportedSchema")]
    [InlineData("{\"id\":\"example-mod\",\"version\":\"1\"}", "UnsupportedSchema")]
    [InlineData("[]", "UnsupportedSchema")]
    [InlineData("{\"schemaVersion\":1,\"id\":\"example-mod\",\"id\":\"second-mod\",\"version\":\"1\"}", "DuplicateJsonKey")]
    [InlineData("{\"schemaVersion\":1,\"id\":\"example-mod\",\"version\":\"1\",\"depends\":{\"other-mod\":\"1\",\"other-mod\":\"2\"}}", "DuplicateJsonKey")]
    [InlineData("{\"schemaVersion\":1,\"id\":\"example-mod\",\"version\":\"1\",\"environment\":\"mystery\"}", "UnknownEnvironment")]
    [InlineData("{\"schemaVersion\":1,\"id\":\"example-mod\",\"version\":\"1\",\"environment\":[\"server\"]}", "UnknownEnvironment")]
    [InlineData("{\"schemaVersion\":1,\"id\":\"example-mod\",\"version\":\"1\",\"futureCondition\":true}", "UnknownMetadata")]
    [InlineData("{\"schemaVersion\":1,\"id\":\"example-mod\",\"version\":\"1\",\"depends\":{\"other-mod\":{\"if\":true}}}", "UnknownDependencyCondition")]
    [InlineData("{\"schemaVersion\":1,\"id\":\"example-mod\",\"version\":\"1\",\"depends\":{\"other-mod\":[]}}", "UnknownDependencyCondition")]
    [InlineData("{\"schemaVersion\":1,\"id\":\"example-mod\",\"version\":\"1\",\"depends\":{\"other-mod\":\">=1.x\"}}", "UnknownVersionConstraint")]
    [InlineData("{\"schemaVersion\":1,\"id\":\"example-mod\",\"version\":\"1\",\"jars\":[{\"file\":\"other.jar\",\"condition\":true}]}", "UnknownNestedCondition")]
    [InlineData("{\"schemaVersion\":1,\"id\":\"example-mod\",\"version\":\"1\",\"jars\":[{\"file\":\"other.jar\"}]}", "MissingNestedJar")]
    [InlineData("{\"schemaVersion\":1,\"id\":\"example-mod\",\"version\":\"1\",\"provides\":[\"example-mod\"]}", "InvalidProvides")]
    public async Task UnknownOrMalformedMetadataBlocks(string json, string code)
    {
        var result = await Inspect(Jar(("fabric.mod.json", Utf8(json))));
        Assert.False(result.IsComplete);
        Assert.Contains(result.Issues, x => x.Code == code);
    }

    [Fact]
    public async Task MetadataSizeAndJsonDepthAreBounded()
    {
        var huge = Minimal[..^1] + ",\"description\":\"" + new string('a', FabricJarInspector.MaxMetadataBytes) + "\"}";
        Assert.Contains((await Inspect(Jar(("fabric.mod.json", Utf8(huge))))).Issues, x => x.Code == "MetadataSizeLimit");
        var deep = Minimal[..^1] + ",\"custom\":{" + string.Concat(Enumerable.Repeat("\"a\":{", 20)) + "\"x\":1" + new string('}', 22);
        Assert.Contains((await Inspect(Jar(("fabric.mod.json", Utf8(deep))))).Issues, x => x.Code == "InvalidMetadata");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CumulativeMetadataBudgetAppliesAcrossNestedJars(bool identicalContent)
    {
        var paths = Enumerable.Range(0, 9).Select(x => $"nested/{x}.jar").ToArray();
        var root = Minimal[..^1] + ",\"jars\":" + JsonSerializer.Serialize(paths.Select(x => new { file = x })) + "}";
        var entries = new List<(string, byte[])> { ("fabric.mod.json", Utf8(root)) };
        var repeated = Jar(("fabric.mod.json", Utf8(JsonSerializer.Serialize(new { schemaVersion = 1, id = "shared-child", version = "1", description = new string('a', 480 * 1024) }))));
        for (var i = 0; i < paths.Length; i++)
        {
            var child = JsonSerializer.Serialize(new { schemaVersion = 1, id = $"child-{i}", version = "1", description = new string('a', 480 * 1024) });
            entries.Add((paths[i], identicalContent ? repeated : Jar(("fabric.mod.json", Utf8(child)))));
        }
        var result = await Inspect(Jar(entries.ToArray()));
        Assert.False(result.IsComplete);
        Assert.Contains(result.Issues, x => x.Code == "MetadataSizeLimit");
        Assert.Equal(9, result.Mods.Count); // root plus eight occurrences, including identical copies
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CumulativeNestedBytesAreBoundedBeforeFourthLargeJarInflates(bool identicalContent)
    {
        var padding = new byte[17 * 1024 * 1024];
        var entries = new List<(string, byte[])>();
        var paths = Enumerable.Range(0, 4).Select(x => $"nested/{x}.jar").ToArray();
        var root = Minimal[..^1] + ",\"jars\":" + JsonSerializer.Serialize(paths.Select(x => new { file = x })) + "}";
        entries.Add(("fabric.mod.json", Utf8(root)));
        var repeated = Jar(("fabric.mod.json", Utf8(Minimal.Replace("example-mod", "shared-child"))), ("opaque.bin", padding));
        for (var i = 0; i < paths.Length; i++)
        {
            var metadata = JsonSerializer.Serialize(new { schemaVersion = 1, id = $"child-{i}", version = "1" });
            entries.Add((paths[i], identicalContent ? repeated : Jar(("fabric.mod.json", Utf8(metadata)), ("opaque.bin", padding))));
        }
        var result = await Inspect(Jar(entries.ToArray()));
        Assert.False(result.IsComplete);
        Assert.Contains(result.Issues, x => x.Code == "NestedSizeLimit");
        Assert.Equal(4, result.Mods.Count); // root plus three inspected children
    }

    [Fact]
    public async Task DataDescriptorsAreValidatedAndReadable()
    {
        using var output = new NonSeekWriteStream();
        using (var archive = new ZipArchive(output, ZipArchiveMode.Create, true))
        {
            using var entry = archive.CreateEntry("fabric.mod.json", CompressionLevel.Optimal).Open();
            entry.Write(Utf8(Minimal));
        }
        var bytes = output.ToArray();
        Assert.Equal(8, BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(8)));
        Assert.True((await Inspect(bytes)).IsComplete);
        var descriptor = Find(bytes, 0x08074b50);
        bytes[descriptor + 4] ^= 1;
        Assert.Contains((await Inspect(bytes)).Issues, x => x.Code == "InvalidZip");
    }

    [Fact]
    public async Task StoredEntryDataDescriptorCannotSupplyNestedProvider()
    {
        // Java ZipInputStream, used for Fabric nested discovery, permits data
        // descriptors only for DEFLATED entries. ZipArchive alone accepts this.
        using var nestedOutput = new NonSeekWriteStream();
        using (var archive = new ZipArchive(nestedOutput, ZipArchiveMode.Create, true))
        {
            using var entry = archive.CreateEntry("fabric.mod.json", CompressionLevel.NoCompression).Open();
            entry.Write(Utf8(Minimal));
        }
        var bytes = nestedOutput.ToArray();
        Assert.Equal(0, BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(8)));
        Assert.NotEqual(0, BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(6)) & 8);
        var result = await Inspect(Wrap(bytes, "outer-mod"));
        Assert.False(result.IsComplete);
        Assert.Contains(result.Issues, x => x.Code == "UnsupportedZip");
        Assert.DoesNotContain(result.Mods, x => x.Id == "example-mod");
    }

    [Theory]
    [InlineData("1")]
    [InlineData("1.2.3")]
    public async Task RootAndNestedDuplicateModIdsAreNeverCollapsed(string nestedVersion)
    {
        var result = await Inspect(Wrap(Jar(("fabric.mod.json", Utf8(Minimal.Replace("1.2.3", nestedVersion)))), "example-mod"));
        Assert.False(result.IsComplete);
        Assert.Contains(result.Issues, x => x.Code == "DuplicateModId");
        Assert.Equal(2, result.Mods.Count);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task IdenticalNestedContentKeepsEveryPathAndEnvironmentAncestor(bool clientParent)
    {
        var shared = Jar(("fabric.mod.json", Utf8(Minimal)));
        var parentMetadata = JsonSerializer.Serialize(new { schemaVersion = 1, id = "parent-mod", version = "1", environment = clientParent ? "client" : "*",
            jars = new[] { new { file = "shared.jar" } } });
        var parent = Jar(("fabric.mod.json", Utf8(parentMetadata)), ("shared.jar", shared));
        var result = await Inspect(Bundle("root-mod", shared, parent));

        Assert.True(result.IsComplete, JsonSerializer.Serialize(result.Issues));
        Assert.Equal(4, result.Mods.Count);
        var copies = result.Mods.Where(x => x.Id == "example-mod").ToArray();
        Assert.Equal(2, copies.Length);
        Assert.Equal(copies[0].ContentSha256, copies[1].ContentSha256);
        Assert.Equal(new[] { "test.jar!/nested/0.jar", "test.jar!/nested/1.jar!/shared.jar" }, copies.Select(x => x.ArchivePath));
        Assert.Equal(new[] { 1, 2 }, copies.Select(x => x.NestedDepth));
        Assert.Equal(clientParent ? FabricEnvironment.Client : FabricEnvironment.Universal, result.Mods.Single(x => x.Id == "parent-mod").Environment);
    }

    [Theory]
    [InlineData("1.2.3")]
    [InlineData("2.0.0")]
    public async Task DifferentNestedContentStillRequiresCandidateSelection(string otherVersion)
    {
        var first = Jar(("fabric.mod.json", Utf8(Minimal)), ("payload.bin", [1]));
        var other = Jar(("fabric.mod.json", Utf8(Minimal.Replace("1.2.3", otherVersion))), ("payload.bin", [2]));
        var result = await Inspect(Bundle("root-mod", first, first, other));

        Assert.False(result.IsComplete);
        Assert.Single(result.Issues, x => x.Code == "DuplicateModId");
        Assert.Equal(4, result.Mods.Count);
        Assert.Equal(result.Mods[1].ContentSha256, result.Mods[2].ContentSha256);
        Assert.NotEqual(result.Mods[1].ContentSha256, result.Mods[3].ContentSha256);
    }

    [Fact]
    public async Task RepeatedNestedContentDoesNotBypassCumulativeEntryBudget()
    {
        var entries = new List<(string, byte[])> { ("fabric.mod.json", Utf8(Minimal)) };
        entries.AddRange(Enumerable.Range(0, FabricJarInspector.MaxArchiveEntries / 2 - 1).Select(x => ($"payload/{x}", Array.Empty<byte>())));
        var shared = Jar(entries.ToArray());
        var result = await Inspect(Bundle("root-mod", shared, shared));

        Assert.False(result.IsComplete);
        Assert.Contains(result.Issues, x => x.Code == "EntryLimit");
        Assert.Equal(2, result.Mods.Count); // root and first occurrence, never the over-budget copy
    }

    [Fact]
    public async Task RepeatedNestedContentDoesNotBypassCumulativeRelationBudget()
    {
        var metadata = JsonSerializer.Serialize(new { schemaVersion = 1, id = "shared-mod", version = "1",
            depends = Enumerable.Range(0, 2048).ToDictionary(x => $"dependency-{x}", _ => "*") });
        var shared = Jar(("fabric.mod.json", Utf8(metadata)));
        var result = await Inspect(Bundle("root-mod", shared, shared, shared));

        Assert.False(result.IsComplete);
        Assert.Contains(result.Issues, x => x.Code == "RelationLimit");
        Assert.Equal(3, result.Mods.Count); // root and two occurrences consume all 4096 relations
    }

    [Fact]
    public async Task RepeatedNestedContentDoesNotBypassDepthBudget()
    {
        var shared = Wrap(Jar(("fabric.mod.json", Utf8(Minimal))), "shared-parent");
        var result = await Inspect(Bundle("root-mod", shared, Wrap(shared, "middle-mod")));

        Assert.False(result.IsComplete);
        Assert.Contains(result.Issues, x => x.Code == "NestedDepthLimit" && x.ArchivePath == "test.jar!/nested/1.jar!/nested.jar");
    }

    [Theory]
    [InlineData("../outside.class")]
    [InlineData("/outside.class")]
    [InlineData("C:/outside.class")]
    [InlineData("a\\outside.class")]
    [InlineData("a//outside.class")]
    [InlineData("a//")]
    [InlineData("a/./outside.class")]
    [InlineData("a/../outside.class")]
    [InlineData("a /outside.class")]
    public async Task UnsafeArchivePathsBlockEvenForUninspectedPayloads(string name)
    {
        var result = await Inspect(Jar(("fabric.mod.json", Utf8(Minimal)), (name, [1, 2, 3])));
        Assert.Contains(result.Issues, x => x.Code == "UnsafePath");
    }

    [Fact]
    public async Task DuplicateAndCaseCollidingEntriesBlock()
    {
        Assert.Contains((await Inspect(Jar(("fabric.mod.json", Utf8(Minimal)), ("fabric.mod.json", Utf8(Minimal))))).Issues, x => x.Code == "DuplicateEntry");
        Assert.Contains((await Inspect(Jar(("fabric.mod.json", Utf8(Minimal)), ("FABRIC.MOD.JSON", Utf8(Minimal))))).Issues, x => x.Code == "DuplicateEntry");
    }

    [Fact]
    public async Task FileDirectoryPrefixAmbiguityBlocks()
    {
        var result = await Inspect(Jar(("fabric.mod.json", Utf8(Minimal)), ("folder", [1]), ("folder/data", [2])));
        Assert.Contains(result.Issues, x => x.Code == "UnsafePath");
    }

    [Fact]
    public async Task EncryptedAndUnsupportedCompressionFlagsBlock()
    {
        var bytes = Jar(("fabric.mod.json", Utf8(Minimal)));
        var central = Find(bytes, 0x02014b50);
        bytes[6] |= 1;
        bytes[central + 8] |= 1;
        Assert.Contains((await Inspect(bytes)).Issues, x => x.Code == "UnsupportedZip");
        bytes = Jar(("fabric.mod.json", Utf8(Minimal)));
        central = Find(bytes, 0x02014b50);
        bytes[8] = 99;
        bytes[central + 10] = 99;
        Assert.Contains((await Inspect(bytes)).Issues, x => x.Code == "UnsupportedZip");
    }

    [Fact]
    public async Task Zip64SymlinksAndOverlapsBlock()
    {
        var bytes = Jar(("fabric.mod.json", Utf8(Minimal)));
        var central = Find(bytes, 0x02014b50);
        bytes[central + 6] = 45;
        Assert.Contains((await Inspect(bytes)).Issues, x => x.Code == "UnsupportedZip");
        bytes = Jar(("fabric.mod.json", Utf8(Minimal)));
        central = Find(bytes, 0x02014b50);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(central + 38), 0xa0000000);
        Assert.Contains((await Inspect(bytes)).Issues, x => x.Code == "UnsafePath");
        bytes = Jar(("fabric.mod.json", Utf8(Minimal)));
        central = Find(bytes, 0x02014b50);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(central + 42), 1);
        Assert.Contains((await Inspect(bytes)).Issues, x => x.Code == "InvalidZip");
    }

    [Fact]
    public async Task LocalCentralDisagreementAndCrcMismatchBlock()
    {
        var bytes = Jar(("fabric.mod.json", Utf8(Minimal)));
        bytes[30] = (byte)'x';
        Assert.Contains((await Inspect(bytes)).Issues, x => x.Code == "InvalidZip");
        bytes = Jar(("fabric.mod.json", Utf8(Minimal)));
        var central = Find(bytes, 0x02014b50);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(14), 123);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(central + 16), 123);
        Assert.Contains((await Inspect(bytes)).Issues, x => x.Code == "CrcMismatch");
    }

    [Fact]
    public async Task ZipBombRatiosTrailingDataAndTruncationBlock()
    {
        using var output = new MemoryStream();
        using (var archive = new ZipArchive(output, ZipArchiveMode.Create, true))
        {
            using var metadata = archive.CreateEntry("fabric.mod.json", CompressionLevel.SmallestSize).Open();
            metadata.Write(Utf8(Minimal[..^1] + ",\"description\":\"" + new string('a', 500_000) + "\"}"));
        }
        Assert.Contains((await Inspect(output.ToArray())).Issues, x => x.Code == "CompressionLimit");
        var valid = Jar(("fabric.mod.json", Utf8(Minimal)));
        Assert.False((await Inspect(valid.Concat(new byte[] { 1 }).ToArray())).IsComplete);
        Assert.False((await Inspect(valid[..^5])).IsComplete);
    }

    [Fact]
    public async Task EntryCountAndArchiveLengthAreBoundedBeforeReadingContents()
    {
        var bytes = Jar(("fabric.mod.json", Utf8(Minimal)));
        var eocd = Find(bytes, 0x06054b50);
        BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(eocd + 8), 16_385);
        BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(eocd + 10), 16_385);
        Assert.Contains((await Inspect(bytes)).Issues, x => x.Code == "EntryLimit");
        using var huge = new OversizeStream();
        Assert.Contains((await _inspector.InspectAsync(huge, "oversize.jar")).Issues, x => x.Code == "ArchiveSize");
    }

    [Fact]
    public async Task CancellationPropagatesAndNeverReturnsSuccessfulPartialResult()
    {
        using var canceled = new CancellationTokenSource();
        canceled.Cancel();
        using var stream = new MemoryStream(Jar(("fabric.mod.json", Utf8(Minimal))));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => _inspector.InspectAsync(stream, "cancel.jar", canceled.Token));
        using var during = new CancellationTokenSource();
        using var cancelOnHash = new CancelOnHashStream(stream.ToArray(), during);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => _inspector.InspectAsync(cancelOnHash, "cancel.jar", during.Token));
    }

    private async Task<FabricJarInspection> Inspect(byte[] bytes)
    {
        using var stream = new MemoryStream(bytes, false);
        return await _inspector.InspectAsync(stream, "test.jar");
    }
    private static byte[] Wrap(byte[] nested, string id)
    {
        var root = JsonSerializer.Serialize(new { schemaVersion = 1, id, version = "1", jars = new[] { new { file = "nested.jar" } } });
        return Jar(("fabric.mod.json", Utf8(root)), ("nested.jar", nested));
    }
    private static byte[] Bundle(string id, params byte[][] nested)
    {
        var paths = Enumerable.Range(0, nested.Length).Select(x => $"nested/{x}.jar").ToArray();
        var metadata = JsonSerializer.Serialize(new { schemaVersion = 1, id, version = "1", jars = paths.Select(x => new { file = x }).ToArray() });
        return Jar(new[] { ("fabric.mod.json", Utf8(metadata)) }.Concat(paths.Select((path, i) => (path, nested[i]))).ToArray());
    }
    private static byte[] Utf8(string value) => Encoding.UTF8.GetBytes(value);
    private static byte[] Jar(params (string Name, byte[] Content)[] entries)
    {
        using var output = new MemoryStream();
        using (var archive = new ZipArchive(output, ZipArchiveMode.Create, true))
            foreach (var item in entries)
            {
                using var entry = archive.CreateEntry(item.Name, CompressionLevel.NoCompression).Open();
                entry.Write(item.Content);
            }
        return output.ToArray();
    }
    private static int Find(byte[] bytes, uint signature)
    {
        for (var i = bytes.Length - 4; i >= 0; i--)
            if (BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(i)) == signature) return i;
        throw new InvalidOperationException("Missing test ZIP record");
    }
    private sealed class NonSeekWriteStream : MemoryStream
    {
        public override bool CanSeek => false;
    }
    private sealed class OversizeStream : MemoryStream
    {
        public override long Length => FabricJarInspector.MaxArchiveBytes + 1;
    }
    private sealed class CancelOnHashStream(byte[] data, CancellationTokenSource source) : MemoryStream(data)
    {
        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            source.Cancel();
            return base.ReadAsync(buffer, cancellationToken);
        }
    }
}
