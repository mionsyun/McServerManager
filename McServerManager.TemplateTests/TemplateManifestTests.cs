using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using McServerManager.Services.Templates;
using Xunit.Abstractions;

namespace McServerManager.TemplateTests;

public sealed class TemplateManifestTests
{
    private static readonly TemplateManifestService Service = new();
    private static int _checks;
    private readonly ITestOutputHelper _output;

    public TemplateManifestTests(ITestOutputHelper output) => _output = output;

    [Fact]
    public async Task EnforcesSchemaAndSafetyContract()
    {
        var count = await RunAsync();
        Assert.True(count > 150, "The complete adversarial fixture suite must run.");
        _output.WriteLine($"Validated {count} manifest assertions, including the provided fixtures.");
    }

    private static async Task<int> RunAsync()
    {
        var fixture = File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Fixtures", "friend-vanilla.maipilot-template.json"));
        var result = Service.Parse(fixture);
        Check(result.IsValid && result.Manifest is not null, "Provided Vanilla fixture must parse.");
        Check(result.Manifest!.Settings.Gamemode == "survival", "Settings must survive parsing.");
        Check(result.ManifestSha256 == Convert.ToHexString(SHA256.HashData(fixture)).ToLowerInvariant(), "Digest must cover exact input bytes.");
        var whitespace = fixture.Concat(new byte[] { 32 }).ToArray();
        Check(Service.Parse(whitespace).ManifestSha256 != result.ManifestSha256, "Formatting changes must change the digest.");
        var negatives = JsonNode.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "negative-schema-fixtures.json")))!.AsArray();
        foreach (var test in negatives)
            Reject(test!["manifest"]!, "Provided negative: " + test["case"]);

        foreach (var runtime in new[] { "vanilla", "paper", "fabric", "forge" })
            Accept(Manifest(runtime), "Pinned " + runtime + " is structurally valid, not installable.");
        foreach (var type in new[] { "neoforge", "quilt", "bedrock", "spigot", "purpur", "unknown" })
        {
            var value = Manifest(); value["runtime"]!["type"] = type;
            Reject(value, "Unknown runtime " + type);
        }
        foreach (var type in new[] { "paper", "fabric", "forge" })
        {
            var value = Manifest(type);
            var pin = type == "fabric" ? "loaderVersion" : "build";
            value["runtime"]!.AsObject().Remove(pin);
            Reject(value, "Missing runtime pin " + type, "MissingRuntimePin");
            foreach (var invalid in new[] { "latest", "1.*", ">=1.2.3", "1.2.3\n", "https://example.invalid/runtime.jar", "../runtime" })
            {
                value = Manifest(type); value["runtime"]![pin] = invalid;
                Reject(value, "Non-exact runtime pin " + invalid);
            }
        }
        var bad = Manifest("fabric"); bad["runtime"]!.AsObject().Remove("installerVersion");
        Reject(bad, "Fabric requires installer pin.", "MissingRuntimePin");
        foreach (var key in new[] { "build", "loaderVersion", "installerVersion" })
        {
            bad = Manifest(); bad["runtime"]![key] = "1.0.0";
            Reject(bad, "Vanilla unexpected runtime pin " + key, "InvalidRuntimePin");
        }
        foreach (var property in new[] { "downloadUrl", "official", "trusted", "jvmArgs", "javaPath", "commands", "scripts", "world", "port" })
        {
            bad = Manifest(); bad[property] = "not permitted";
            Reject(bad, "Unknown root property " + property, "UnknownProperty");
        }
        foreach (var property in new[] { "rcon.password", "server-ip", "server-port", "online-mode", "motd", "resource-pack", "white-list", "level-name", "enable-command-block" })
        {
            bad = Manifest(); bad["settings"]![property] = "not permitted";
            Reject(bad, "Forbidden setting " + property, "UnknownProperty");
        }
        foreach (var property in Manifest().AsObject().Select(pair => pair.Key).ToArray())
        {
            bad = Manifest(); bad.AsObject().Remove(property);
            Reject(bad, "Missing root property " + property, "MissingProperty");
            bad = Manifest(); bad[property] = null;
            Reject(bad, "Null root property " + property);
        }
        foreach (var (key, minimum, maximum) in new[] { ("maxPlayers", 1, 1000), ("viewDistance", 2, 32), ("simulationDistance", 2, 32), ("spawnProtection", 0, 256) })
        {
            foreach (var boundary in new[] { minimum, maximum })
            {
                bad = Manifest(); bad["settings"]![key] = boundary;
                Accept(bad, "Allowed " + key + " boundary");
            }
            foreach (var boundary in new[] { minimum - 1, maximum + 1 })
            {
                bad = Manifest(); bad["settings"]![key] = boundary;
                Reject(bad, "Rejected " + key + " boundary");
            }
            bad = Manifest(); bad["settings"]![key] = 2.5;
            Reject(bad, "Fractional integer " + key);
            bad = Manifest(); bad["settings"]![key] = "8";
            Reject(bad, "String integer " + key);
        }
        bad = Manifest(); bad["settings"]!["pvp"] = "true";
        Reject(bad, "Boolean string is invalid.");
        bad = Manifest(); bad["settings"]!["gamemode"] = "creative";
        Check(Service.Parse(Bytes(bad)).Manifest!.Settings.Gamemode == "creative", "Creative recipe must preserve actual gamemode.");
        bad = Manifest(); bad["name"] = string.Concat(Enumerable.Repeat("🌳", 80));
        Accept(bad, "Name uses Unicode scalar count.");
        bad["name"] = new string('x', 81); Reject(bad, "Name length cap.");
        bad = Manifest(); bad["description"] = new string('x', 1001); Reject(bad, "Description length cap.");
        bad = Manifest(); bad["templateId"] = "not-a-uuid"; Reject(bad, "UUID validation.");
        bad = Manifest(); bad["revision"] = 0; Reject(bad, "Revision minimum.");
        bad = Manifest(); bad["revision"] = long.MaxValue; Reject(bad, "Revision integer bound.");
        bad = Manifest(); bad["schemaVersion"] = "1.1"; Reject(bad, "Unknown schema version.");
        bad = Manifest(); bad["runtime"]!["minecraftVersion"] = "1.21.1\n"; Reject(bad, "Version newline must not bypass anchors.");

        var text = Manifest().ToJsonString();
        RejectBytes(Encoding.UTF8.GetBytes(text.Replace("\"schemaVersion\":\"1.0\"", "\"schemaVersion\":\"1.0\",\"schemaVersion\":\"1.0\"")), "Duplicate root key.", "DuplicateProperty");
        RejectBytes(Encoding.UTF8.GetBytes(text.Replace("\"schemaVersion\":\"1.0\"", "\"schemaVersion\":\"1.0\",\"schema\\u0056ersion\":\"1.0\"")), "Escaped duplicate key.", "DuplicateProperty");
        RejectBytes(Encoding.UTF8.GetBytes(text.Replace("\"settings\":{}", "\"settings\":{\"pvp\":true,\"pvp\":false}")), "Nested duplicate key.", "DuplicateProperty");
        RejectBytes(Encoding.UTF8.GetBytes(text.Replace("\"name\":\"Fixture\"", "\"name\":\"\\uD800\"")), "Unpaired Unicode surrogate.");
        RejectBytes(Encoding.UTF8.GetBytes(text.Replace("\"name\":\"Fixture\"", "\"\\uD800\":\"Fixture\"")), "Invalid Unicode key.");
        RejectBytes([0xff, 0xfe, 0x7b], "Invalid UTF-8.", "InvalidUtf8");
        RejectBytes([], "Empty JSON.", "InvalidJson");
        RejectBytes(Encoding.UTF8.GetBytes(text + "{}"), "Multiple JSON roots.", "InvalidJson");
        RejectBytes(Encoding.UTF8.GetBytes("/* comment */" + text), "JSON comments.", "InvalidJson");
        RejectBytes(Encoding.UTF8.GetBytes(text[..^1] + ",}"), "Trailing comma.", "InvalidJson");
        RejectBytes(Encoding.UTF8.GetBytes(new string('[', 17) + "0" + new string(']', 17)), "Excess JSON depth.", "InvalidJson");

        TestAddons();
        await TestStreamsAsync(fixture);
        return _checks;
    }

    private static void TestAddons()
    {
        var value = WithAddons(Addon("a"));
        Accept(value, "Pinned Modrinth addon syntax.");
        var addonJson = value.ToJsonString();
        foreach (var duplicate in new[] { "\"type\":\"fabric\"", "\"entryId\":\"a\"", "\"provider\":\"modrinth\"", "\"versionId\":\"versiona\"" })
            RejectBytes(Encoding.UTF8.GetBytes(addonJson.Replace(duplicate, duplicate + "," + duplicate)), "Duplicate key at every object level.", "DuplicateProperty");

        foreach (var name in new[] { "../x.jar", "..\\x.jar", "C:x.jar", "x:stream.jar", "NUL.jar", "aux.more.jar", "CON .jar", "COM1.jar", "lpt9.jar", "COM¹.jar", "CONIN$.jar", "CONOUT$.jar", "x?.jar", "x*.jar", "x|y.jar", "x<y.jar", "x>y.jar", "x\"y.jar", "x\u0000.jar", "x\n.jar", "x\u007f.jar", " x.jar", "x.jar ", "x.jar.", "x.JAR", "x.exe", ".jar", new string('x', 181) + ".jar" })
        {
            value = WithAddons(Addon("a")); value["addons"]![0]!["source"]!["fileName"] = name;
            Reject(value, "Unsafe Windows filename " + name);
        }
        foreach (var field in new[] { "projectId", "versionId" })
            foreach (var invalid in new[] { "../escape", "project-slug", "id\n", "https://example.invalid", "", new string('a', 65) })
            {
                value = WithAddons(Addon("a")); value["addons"]![0]!["source"]![field] = invalid;
                Reject(value, "Invalid provider identifier " + field);
            }
        value = WithAddons(Addon("a")); value["addons"]![0]!["source"]!["downloadUrl"] = "https://example.invalid";
        Reject(value, "No URL field.", "UnknownProperty");
        value = WithAddons(Addon("a")); value["addons"]![0]!["source"] = new JsonObject
        {
            ["provider"] = "curseforge", ["projectId"] = "123", ["fileId"] = "456", ["fileName"] = "a.jar", ["sha1"] = new string('a', 40)
        };
        Reject(value, "CurseForge must remain disabled.", "ProviderDisabled");
        foreach (var invalid in new[] { new string('A', 64), new string('a', 63), new string('a', 64) + "\n" })
        {
            value = WithAddons(Addon("a")); value["addons"]![0]!["sha256"] = invalid;
            Reject(value, "Strict SHA-256.");
        }
        foreach (var size in new[] { 0L, TemplatePolicy.MaxAddonBytes + 1, long.MaxValue })
        {
            value = WithAddons(Addon("a")); value["addons"]![0]!["sizeBytes"] = size;
            Reject(value, "Per-file size limits.");
        }
        var huge = Enumerable.Range(0, 5).Select(index => Addon("a" + index)).ToArray();
        foreach (var addon in huge) addon["sizeBytes"] = TemplatePolicy.MaxAddonBytes;
        Reject(WithAddons(huge), "Total size cap.", "TotalSizeExceeded");
        Accept(WithAddons(huge[..4]), "Exact 2 GiB declared total.");
        Accept(WithAddons(Enumerable.Range(0, 256).Select(index => Addon("a" + index)).ToArray()), "256 addon boundary.");
        Reject(WithAddons(Enumerable.Range(0, 257).Select(index => Addon("a" + index)).ToArray()), "257 addon count rejected.", "LimitExceeded");
        value = WithAddons(Addon("a")); value["runtime"] = Manifest()["runtime"]!.DeepClone();
        Reject(value, "Vanilla cannot contain addons.", "IncompatibleAddonKind");
        value = WithAddons(Addon("a")); value["addons"]![0]!["kind"] = "plugin";
        Reject(value, "Fabric cannot contain plugins.", "IncompatibleAddonKind");
        value["runtime"] = Manifest("paper")["runtime"]!.DeepClone();
        Accept(value, "Paper plugin shape only, not installation support.");
        Reject(WithAddons(Addon("a", "missing")), "Missing graph reference.", "MissingDependency");
        Reject(WithAddons(Addon("a", "a")), "Self cycle.", "DependencyCycle");
        Reject(WithAddons(Addon("a", "b"), Addon("b", "a")), "Two-entry cycle.", "DependencyCycle");
        Reject(WithAddons(Addon("a", "b", "b"), Addon("b")), "Duplicate requires.", "DuplicateDependency");
        Reject(WithAddons(Addon("a"), Addon("a")), "Duplicate entry identifier.", "DuplicateEntryId");
        value = WithAddons(Addon("a"), Addon("b")); value["addons"]![1]!["source"]!["projectId"] = "projecta";
        Reject(value, "Duplicate provider project.", "DuplicateProject");
        value = WithAddons(Addon("a"), Addon("b")); value["addons"]![1]!["sha256"] = value["addons"]![0]!["sha256"]!.DeepClone();
        Reject(value, "Duplicate content.", "DuplicateArtifact");
        value = WithAddons(Addon("a"), Addon("b")); value["addons"]![1]!["source"]!["fileName"] = "A.jar";
        Reject(value, "Windows case-insensitive file collision.", "DuplicateFileName");
        Accept(Chain(16, false), "Dependency depth boundary.");
        Reject(Chain(17, false), "Dependency depth cap.", "DependencyDepthExceeded");
        Reject(Chain(17, true), "Memoized child must not bypass dependency depth.", "DependencyDepthExceeded");
        Accept(WithAddons(Addon("a", "c"), Addon("b", "c"), Addon("c")), "Shared dependency is not a cycle.");
    }

    private static async Task TestStreamsAsync(byte[] fixture)
    {
        using var partial = new ChunkedStream(fixture, 7);
        Check((await Service.ReadAsync(partial)).IsValid, "Partial reads must parse.");
        Check(!partial.WasDisposed, "Parser must leave caller's stream open.");
        var exact = fixture.Concat(Enumerable.Repeat((byte)32, TemplatePolicy.MaxManifestBytes - fixture.Length)).ToArray();
        using var exactStream = new ChunkedStream(exact, 16384);
        Check((await Service.ReadAsync(exactStream)).IsValid, "Exact byte-limit manifest accepted.");
        var oversized = exact.Concat(new byte[] { 32, 32, 32 }).ToArray();
        RejectBytes(oversized, "Oversized memory input.", "ManifestTooLarge");
        using var largeStream = new ChunkedStream(oversized, 16384);
        var result = await Service.ReadAsync(largeStream);
        Check(!result.IsValid && result.Issues[0].Code == "ManifestTooLarge", "Oversized stream rejected.");
        Check(largeStream.BytesRead == TemplatePolicy.MaxManifestBytes + 1, "Must never read beyond limit plus one byte.");
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        using var untouched = new ChunkedStream(fixture, 8);
        try { await Service.ReadAsync(untouched, cancellation.Token); throw new Exception("Expected cancellation."); }
        catch (OperationCanceledException) { Check(untouched.BytesRead == 0, "Pre-cancellation reads no bytes."); }
        using var middleCancellation = new CancellationTokenSource();
        using var interrupted = new ChunkedStream(fixture, 8, middleCancellation);
        try { await Service.ReadAsync(interrupted, middleCancellation.Token); throw new Exception("Expected mid-read cancellation."); }
        catch (OperationCanceledException) { Check(interrupted.BytesRead == 8, "Cancellation between chunks stops reading."); }
        using var failed = new ChunkedStream(fixture, 8, failReads: true);
        try { await Service.ReadAsync(failed); throw new Exception("Expected I/O exception."); }
        catch (IOException) { Check(true, "I/O exceptions propagate to UI."); }
    }

    private static JsonObject Chain(int count, bool reversed)
    {
        var entries = Enumerable.Range(0, count).Select(index => index + 1 < count ? Addon("a" + index, "a" + (index + 1)) : Addon("a" + index));
        return WithAddons((reversed ? entries.Reverse() : entries).ToArray());
    }

    private static JsonObject Manifest(string runtime = "vanilla")
    {
        var spec = new JsonObject { ["type"] = runtime, ["minecraftVersion"] = "1.21.1" };
        if (runtime == "paper") spec["build"] = "123";
        if (runtime == "forge") spec["build"] = "52.0.1";
        if (runtime == "fabric") { spec["loaderVersion"] = "0.16.5"; spec["installerVersion"] = "1.0.1"; }
        return new JsonObject
        {
            ["schemaVersion"] = "1.0", ["templateId"] = "7b4643cb-6e70-4a4f-bd3d-dbf6f3b9a1bc", ["revision"] = 1,
            ["name"] = "Fixture", ["description"] = "Synthetic offline test data; not an official recipe.", ["edition"] = "java",
            ["runtime"] = spec, ["settings"] = new JsonObject(), ["addons"] = new JsonArray()
        };
    }

    private static JsonObject Addon(string id, params string[] requires) => new()
    {
        ["entryId"] = id, ["kind"] = "mod", ["selection"] = "direct", ["sha256"] = Hash(id), ["sizeBytes"] = 1,
        ["source"] = new JsonObject { ["provider"] = "modrinth", ["projectId"] = "project" + id, ["versionId"] = "version" + id, ["fileName"] = id + ".jar", ["sha512"] = new string('a', 128) },
        ["requires"] = new JsonArray(requires.Select(value => (JsonNode?)JsonValue.Create(value)).ToArray())
    };

    private static JsonObject WithAddons(params JsonObject[] addons)
    {
        var value = Manifest("fabric"); value["addons"] = new JsonArray(addons.Select(addon => addon.DeepClone()).ToArray()); return value;
    }
    private static string Hash(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();
    private static byte[] Bytes(JsonNode value) => Encoding.UTF8.GetBytes(value.ToJsonString());
    private static void Accept(JsonNode value, string label)
    {
        var result = Service.Parse(Bytes(value));
        Check(result.IsValid && result.Manifest is not null && result.Issues.Count == 0, label + " " + string.Join(", ", result.Issues));
    }
    private static void Reject(JsonNode value, string label, string? code = null) => RejectBytes(Bytes(value), label, code);
    private static void RejectBytes(byte[] bytes, string label, string? code = null)
    {
        var result = Service.Parse(bytes);
        Check(!result.IsValid && result.Manifest is null && result.ManifestSha256 is null && result.Issues.Count > 0 &&
            (code is null || result.Issues[0].Code == code), label + " Actual: " + string.Join(", ", result.Issues));
    }
    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
        _checks++;
    }

    private sealed class ChunkedStream(byte[] bytes, int chunkSize, CancellationTokenSource? cancelAfterRead = null, bool failReads = false) : Stream
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
            if (failReads) throw new IOException("Synthetic read failure.");
            var count = Math.Min(Math.Min(buffer.Length, chunkSize), bytes.Length - BytesRead);
            bytes.AsMemory(BytesRead, count).CopyTo(buffer);
            BytesRead += count;
            if (count > 0) cancelAfterRead?.Cancel();
            return ValueTask.FromResult(count);
        }
        protected override void Dispose(bool disposing) { WasDisposed = true; base.Dispose(disposing); }
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override void Flush() => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
