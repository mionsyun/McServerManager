using System.IO.Compression;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using McServerManager.Models.Editions;
using McServerManager.Models.Participants;
using McServerManager.Services.Editions;
using McServerManager.Services.Participants;

namespace McServerManager.TemplateTests.Participants;

public sealed class ParticipantListZipTests
{
    private static readonly ParticipantClientDefinitionService Parser = new();

    [Theory]
    [InlineData(AppEdition.Free)]
    [InlineData(AppEdition.Pro)]
    public async Task ExportsOnlyThreeFixedUtf8ReferenceEntriesInBothEditions(AppEdition edition)
    {
        var definition = ParseValid();
        var service = new ParticipantListZipService(new EditionPolicy(edition));
        var bytes = await service.CreateAsync(definition);
        Assert.Equal(bytes, await service.CreateAsync(definition));
        Assert.InRange(bytes.Length, 1, ParticipantDefinitionPolicy.MaxZipBytes);
        using var archive = new ZipArchive(new MemoryStream(bytes), ZipArchiveMode.Read);
        Assert.Equal(new[] { "README.txt", "mods.txt", "manifest.json" }, archive.Entries.Select(entry => entry.FullName));
        foreach (var entry in archive.Entries)
        {
            Assert.DoesNotContain("/", entry.FullName);
            Assert.DoesNotContain("\\", entry.FullName);
            Assert.Equal(1980, entry.LastWriteTime.Year);
            var content = ReadEntry(entry);
            Assert.DoesNotContain('\0', content);
            Assert.DoesNotContain('\r', content);
            Assert.DoesNotContain('\uFEFF', content);
        }

        var readme = ReadEntry(archive.GetEntry("README.txt")!);
        Assert.Contains("Minecraft Java Edition: 1.21.1", readme);
        Assert.Contains("fabric 0.16.9", readme);
        Assert.Contains("Java: 21.0.2", readme);
        Assert.Contains("入力形式の検証のみ", readme);
        Assert.Contains("特定の外部ランチャーやアカウント連携は必要ありません", readme);
        Assert.Contains("依存関係の完全性", readme);
        Assert.Contains("未検証", readme);

        var list = ReadEntry(archive.GetEntry("mods.txt")!);
        Assert.Contains("サンプル MOD", list);
        Assert.Contains("1.2.3+fabric", list);
        Assert.Contains("https://modrinth.com/mod/AbCd1234/version/EfGh5678", list);
        using var manifest = JsonDocument.Parse(ReadEntry(archive.GetEntry("manifest.json")!));
        var root = manifest.RootElement;
        Assert.Equal("explicit-client-definition", root.GetProperty("source").GetString());
        Assert.Equal("input-validated-only", root.GetProperty("readiness").GetString());
        Assert.False(root.GetProperty("providerMetadataVerified").GetBoolean());
        Assert.False(root.GetProperty("dependencyCompletenessVerified").GetBoolean());
        Assert.False(root.GetProperty("runtimeCompatibilityVerified").GetBoolean());
        Assert.Equal(definition.InputSha256, root.GetProperty("inputSha256").GetString());
        var mod = root.GetProperty("mods")[0];
        var uri = new Uri(mod.GetProperty("officialVersionPage").GetString()!);
        Assert.Equal("https", uri.Scheme);
        Assert.Equal("modrinth.com", uri.Host);
        Assert.Empty(uri.Query);
        Assert.Empty(uri.Fragment);
        Assert.Equal("required", mod.GetProperty("clientSide").GetString());
    }

    [Fact]
    public void ValidatedDefinitionIsOpaqueImmutableAndSnapshotsInput()
    {
        var input = Encoding.UTF8.GetBytes(Fixture().ToJsonString());
        var originalDigest = Convert.ToHexString(SHA256.HashData(input)).ToLowerInvariant();
        var parsed = Parser.Parse(input);
        Array.Fill(input, (byte)0);
        var definition = Assert.IsType<ParticipantClientDefinition>(parsed.Definition);
        Assert.Equal(originalDigest, definition.InputSha256);
        Assert.Equal(ParticipantDefinitionReadiness.InputValidatedOnly, definition.Readiness);
        Assert.Throws<NotSupportedException>(() => ((IList<ParticipantClientMod>)definition.Mods).Clear());
        foreach (var type in new[] { typeof(ParticipantClientDefinition), typeof(ParticipantClientRuntime), typeof(ParticipantClientMod) })
        {
            Assert.True(type.IsSealed);
            Assert.Empty(type.GetConstructors(BindingFlags.Public | BindingFlags.Instance));
            Assert.All(type.GetProperties(), property => Assert.False(property.CanWrite));
        }
    }

    [Theory]
    [InlineData("../other")]
    [InlineData("AbCd1234/evil")]
    [InlineData("AbCd1234?token=x")]
    [InlineData("https://evil.invalid")]
    [InlineData("AbCd1234\n")]
    [InlineData("AbCd1234\\")]
    [InlineData("AbCd1234%")]
    [InlineData("AbCd１２３４")]
    [InlineData("")]
    [InlineData("short")]
    [InlineData("AbCd12345")]
    public void RejectsMalformedProviderIds(string value)
    {
        foreach (var key in new[] { "projectId", "versionId" })
        {
            var json = Fixture();
            json["mods"]![0]![key] = value;
            Reject(json);
        }
    }

    [Theory]
    [InlineData("A\nOfficial URL: https://evil.invalid")]
    [InlineData("A\rB")]
    [InlineData("A\tB")]
    [InlineData("../evil.jar")]
    [InlineData("C:\\evil.jar")]
    [InlineData("folder/mod")]
    [InlineData("https://evil.invalid")]
    [InlineData("<script>alert(1)</script>")]
    [InlineData("[link](https://evil.invalid)")]
    [InlineData("$(execute)")]
    [InlineData("`execute`")]
    [InlineData("A\u2028B")]
    [InlineData("A\u202EB")]
    [InlineData("A\u0000B")]
    [InlineData(" . ")]
    [InlineData("..")]
    public void RejectsNameAndNotePathMarkupControlAndUrlInjection(string value)
    {
        var root = Fixture(); root["name"] = value; Reject(root);
        var mod = Fixture(); mod["mods"]![0]!["name"] = value; Reject(mod);
        var note = Fixture(); note["mods"]![0]!["note"] = value; Reject(note);
    }

    [Theory]
    [InlineData("latest")]
    [InlineData("1.x")]
    [InlineData("1.*")]
    [InlineData(">=1.2.3")]
    [InlineData("1.2.3\n")]
    [InlineData("../1.2.3")]
    [InlineData("https://example.invalid")]
    public void RejectsLooseOrUnsafeVersions(string value)
    {
        foreach (var key in new[] { "minecraftVersion", "loaderVersion", "javaVersion" })
        {
            var json = Fixture(); json["runtime"]![key] = value; Reject(json);
        }
        var mod = Fixture(); mod["mods"]![0]!["version"] = value; Reject(mod);
    }

    [Fact]
    public void RejectsUnsupportedServerDefinitionsProvidersAndMissingFields()
    {
        var root = Fixture(); root["definitionKind"] = "server"; Reject(root);
        root = Fixture(); root["schemaVersion"] = "2.0"; Reject(root);
        foreach (var loader in new[] { "vanilla", "paper", "bedrock", "neoforge", "quilt", "Fabric" })
        {
            root = Fixture(); root["runtime"]!["loader"] = loader; Reject(root);
        }
        foreach (var clientSide in new[] { "server", "unsupported", "unknown", "both" })
        {
            root = Fixture(); root["mods"]![0]!["clientSide"] = clientSide; Reject(root);
        }
        root = Fixture(); root["mods"]![0]!["provider"] = "curseforge"; Reject(root);
        root = Fixture(); root["runtime"]!["javaVersion"] = "21"; Reject(root);
        root = Fixture(); root["runtime"]!["javaVersion"] = "21.0"; Reject(root);
        foreach (var key in Fixture().Select(pair => pair.Key))
        {
            root = Fixture(); root.Remove(key); Reject(root);
            root = Fixture(); root[key] = null; Reject(root);
        }
        foreach (var key in Fixture()["runtime"]!.AsObject().Select(pair => pair.Key))
        {
            root = Fixture(); root["runtime"]!.AsObject().Remove(key); Reject(root);
        }
        foreach (var key in Fixture()["mods"]![0]!.AsObject().Select(pair => pair.Key).Where(key => key != "note"))
        {
            root = Fixture(); root["mods"]![0]!.AsObject().Remove(key); Reject(root);
        }
    }

    [Fact]
    public void RejectsUnknownDuplicateFieldsAndDuplicateProjectsOrVersions()
    {
        foreach (var key in new[] { "url", "downloadUrl", "jar", "fileName", "commands", "scripts", "verified", "serverArtifacts" })
        {
            var root = Fixture(); root[key] = "untrusted"; Reject(root);
            root = Fixture(); root["runtime"]![key] = "untrusted"; Reject(root);
            root = Fixture(); root["mods"]![0]![key] = "untrusted"; Reject(root);
        }
        var json = Fixture().ToJsonString();
        RejectText(json.Replace("\"definitionKind\":\"client\"", "\"definitionKind\":\"client\",\"definitionKind\":\"client\"", StringComparison.Ordinal));
        RejectText(json.Replace("\"loader\":\"fabric\"", "\"loader\":\"fabric\",\"lo\\u0061der\":\"fabric\"", StringComparison.Ordinal));
        RejectText(json.Replace("\"clientSide\":\"required\"", "\"clientSide\":\"required\",\"clientSide\":\"optional\"", StringComparison.Ordinal));
        foreach (var sameProject in new[] { false, true })
        {
            var root = Fixture();
            var duplicate = root["mods"]![0]!.DeepClone();
            duplicate[sameProject ? "versionId" : "projectId"] = "IjKl9012";
            root["mods"]!.AsArray().Add(duplicate);
            Reject(root);
        }
    }

    [Fact]
    public async Task EnforcesAllBoundsIncludingExactByteLimitAndModCount()
    {
        var json = Fixture();
        json["name"] = new string('a', ParticipantDefinitionPolicy.MaxNameLength);
        json["mods"]![0]!["name"] = new string('b', ParticipantDefinitionPolicy.MaxNameLength);
        json["mods"]![0]!["note"] = new string('c', ParticipantDefinitionPolicy.MaxNoteLength);
        json["mods"]![0]!["version"] = new string('v', ParticipantDefinitionPolicy.MaxVersionLength);
        Assert.True(Parse(json).IsValid);
        json["name"] = new string('a', ParticipantDefinitionPolicy.MaxNameLength + 1); Reject(json);
        json = Fixture(); json["mods"]![0]!["name"] = new string('n', ParticipantDefinitionPolicy.MaxNameLength + 1); Reject(json);
        json = Fixture(); json["mods"]![0]!["note"] = new string('n', ParticipantDefinitionPolicy.MaxNoteLength + 1); Reject(json);
        json = Fixture(); json["mods"]![0]!["version"] = new string('v', ParticipantDefinitionPolicy.MaxVersionLength + 1); Reject(json);

        json = Fixture();
        var mods = new JsonArray();
        for (var index = 0; index < ParticipantDefinitionPolicy.MaxMods; index++)
        {
            var mod = Fixture()["mods"]![0]!.DeepClone();
            mod["projectId"] = $"P{index:0000000}";
            mod["versionId"] = $"V{index:0000000}";
            mods.Add(mod);
        }
        json["mods"] = mods;
        var maximumDefinition = Parse(json);
        Assert.True(maximumDefinition.IsValid);
        Assert.InRange((await NewExporter().CreateAsync(maximumDefinition.Definition!)).Length, 1, ParticipantDefinitionPolicy.MaxZipBytes);
        mods.Add(Fixture()["mods"]![0]!.DeepClone()); Reject(json);

        var bytes = Encoding.UTF8.GetBytes(Fixture().ToJsonString());
        var boundary = Enumerable.Repeat((byte)' ', ParticipantDefinitionPolicy.MaxDefinitionBytes).ToArray();
        bytes.CopyTo(boundary, 0);
        Assert.True(Parser.Parse(boundary).IsValid);
        Assert.False(Parser.Parse(boundary.Concat(new byte[] { 32 }).ToArray()).IsValid);
        using var overlong = new CountingNonSeekableStream(boundary.Concat(new byte[] { 32, 32, 32 }).ToArray());
        Assert.False((await Parser.ReadAsync(overlong)).IsValid);
        Assert.Equal(ParticipantDefinitionPolicy.MaxDefinitionBytes + 1, overlong.BytesRead);
        Assert.False(overlong.WasDisposed);
    }

    [Fact]
    public async Task ReadsPartialNonSeekableStreamsWithoutOwningThemAndAllowsEmptyExplicitList()
    {
        var json = Fixture(); json["mods"] = new JsonArray();
        using var stream = new CountingNonSeekableStream(Encoding.UTF8.GetBytes(json.ToJsonString()), chunkSize: 7);
        var parsed = await Parser.ReadAsync(stream);
        Assert.True(parsed.IsValid);
        Assert.Empty(parsed.Definition!.Mods);
        Assert.False(stream.WasDisposed);
        using var archive = new ZipArchive(new MemoryStream(await NewExporter().CreateAsync(parsed.Definition)), ZipArchiveMode.Read);
        Assert.Contains("クライアント MOD は指定されていません", ReadEntry(archive.GetEntry("mods.txt")!));
    }

    [Fact]
    public async Task RejectsMalformedEncodingJsonAndPropagatesReadFailures()
    {
        foreach (var text in new[] { "{}", "[]", "null", "{", "/* comment */ {}", "{\"name\":\"\\ud800\"}",
            "{\"\\ud800\":true}", new string('[', 9) + new string(']', 9) }) RejectText(text);
        var bytes = Encoding.UTF8.GetBytes(Fixture().ToJsonString());
        bytes[0] = 0xff;
        Assert.False(Parser.Parse(bytes).IsValid);
        RejectText(Fixture().ToJsonString().Replace("\"name\":", "\"name\":\"\\ud800\",\"other\":", StringComparison.Ordinal));
        var scalarFixture = Fixture(); scalarFixture["name"] = "ReplaceThis";
        RejectText(scalarFixture.ToJsonString().Replace("ReplaceThis", "\\ud800", StringComparison.Ordinal));
        RejectText(scalarFixture.ToJsonString().Replace("ReplaceThis", "\\udc00", StringComparison.Ordinal));
        RejectText(Fixture().ToJsonString().Replace("\"schemaVersion\":\"1.0\",", "\"schemaVersion\":\"1.0\",,", StringComparison.Ordinal));
        using var stream = new CountingNonSeekableStream(Encoding.UTF8.GetBytes(Fixture().ToJsonString()), throwOnRead: true);
        await Assert.ThrowsAsync<IOException>(() => Parser.ReadAsync(stream));
    }

    [Fact]
    public async Task CancellationOrDeniedPolicyReturnsNoPartialArchive()
    {
        using var cancelled = new CancellationTokenSource(); cancelled.Cancel();
        Assert.Throws<OperationCanceledException>(() => Parser.Parse(Encoding.UTF8.GetBytes(Fixture().ToJsonString()), cancelled.Token));
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(Fixture().ToJsonString()));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Parser.ReadAsync(stream, cancelled.Token));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => NewExporter().CreateAsync(ParseValid(), cancelled.Token));

        using var midExport = new CancellationTokenSource();
        byte[]? result = null;
        var service = new ParticipantListZipService(new TestPolicy(allow: true, midExport.Cancel));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => result = await service.CreateAsync(ParseValid(), midExport.Token));
        Assert.Null(result);
        var denied = new TestPolicy(allow: false);
        await Assert.ThrowsAsync<InvalidOperationException>(() => new ParticipantListZipService(denied).CreateAsync(ParseValid()));
        Assert.Equal(EditionCapability.ParticipantPackExport, denied.LastCapability);
    }

    [Fact]
    public async Task CancellationDuringReadReturnsNoDefinitionAndLeavesCallerStreamOpen()
    {
        using var cancellation = new CancellationTokenSource();
        using var input = new CountingNonSeekableStream(Encoding.UTF8.GetBytes(Fixture().ToJsonString()),
            chunkSize: 7, onRead: cancellation.Cancel);
        ParticipantDefinitionValidationResult? result = null;
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => result = await Parser.ReadAsync(input, cancellation.Token));
        Assert.Null(result);
        Assert.False(input.WasDisposed);
        Assert.Equal(7, input.BytesRead);
    }

    [Fact]
    public async Task ForgeOptionalDeclarationsRemainUnverifiedAndListOrderIsStable()
    {
        var json = Fixture();
        json["runtime"]!["loader"] = "forge";
        json["runtime"]!["loaderVersion"] = "47.2.0";
        json["runtime"]!["javaVersion"] = "17.0.12+7";
        json["mods"]![0]!["clientSide"] = "optional";
        json["mods"]![0]!.AsObject().Remove("note");
        var earlier = json["mods"]![0]!.DeepClone();
        earlier["projectId"] = "AAAA1234";
        earlier["versionId"] = "AAAA5678";
        json["mods"]!.AsArray().Add(earlier);
        var parsed = Parse(json);
        Assert.True(parsed.IsValid);
        using var archive = new ZipArchive(new MemoryStream(await NewExporter().CreateAsync(parsed.Definition!)), ZipArchiveMode.Read);
        var list = ReadEntry(archive.GetEntry("mods.txt")!);
        Assert.True(list.IndexOf("AAAA1234", StringComparison.Ordinal) < list.IndexOf("AbCd1234", StringComparison.Ordinal));
        Assert.Contains("任意", list);
        Assert.Contains("未照合", list);
        Assert.DoesNotContain("備考", list);
    }

    private static ParticipantListZipService NewExporter() => new(new EditionPolicy(AppEdition.Free));
    private static ParticipantClientDefinition ParseValid() => Parse(Fixture()).Definition!;
    private static ParticipantDefinitionValidationResult Parse(JsonObject json) => Parser.Parse(Encoding.UTF8.GetBytes(json.ToJsonString()));
    private static void Reject(JsonObject json) => RejectText(json.ToJsonString());
    private static void RejectText(string text)
    {
        var result = Parser.Parse(Encoding.UTF8.GetBytes(text));
        Assert.False(result.IsValid);
        Assert.Null(result.Definition);
        Assert.NotEmpty(result.Issues);
    }
    private static string ReadEntry(ZipArchiveEntry entry)
    {
        using var stream = entry.Open();
        using var reader = new StreamReader(stream, new UTF8Encoding(false, true), detectEncodingFromByteOrderMarks: false);
        return reader.ReadToEnd();
    }
    private static JsonObject Fixture() => JsonNode.Parse("""
        {
          "schemaVersion": "1.0",
          "definitionKind": "client",
          "name": "友達用クライアント",
          "runtime": {"minecraftVersion": "1.21.1", "loader": "fabric", "loaderVersion": "0.16.9", "javaVersion": "21.0.2"},
          "mods": [{"provider": "modrinth", "projectId": "AbCd1234", "versionId": "EfGh5678", "name": "サンプル MOD", "version": "1.2.3+fabric", "clientSide": "required", "note": "管理者に確認してください。"}]
        }
        """)!.AsObject();

    private sealed class TestPolicy(bool allow, Action? onCheck = null) : IEditionPolicy
    {
        public AppEdition Edition => AppEdition.Free;
        public string DisplayName => "Test";
        public EditionCapability? LastCapability { get; private set; }
        public bool Allows(EditionCapability capability)
        {
            LastCapability = capability;
            onCheck?.Invoke();
            return allow;
        }
    }

    private sealed class CountingNonSeekableStream(byte[] bytes, int chunkSize = 8192, bool throwOnRead = false, Action? onRead = null) : Stream
    {
        public int BytesRead { get; private set; }
        public bool WasDisposed { get; private set; }
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (throwOnRead) throw new IOException("Test read failure.");
            var count = Math.Min(buffer.Length, Math.Min(chunkSize, bytes.Length - BytesRead));
            bytes.AsMemory(BytesRead, count).CopyTo(buffer);
            BytesRead += count;
            onRead?.Invoke();
            return ValueTask.FromResult(count);
        }
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override void Flush() => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        protected override void Dispose(bool disposing) { WasDisposed = true; base.Dispose(disposing); }
    }
}
