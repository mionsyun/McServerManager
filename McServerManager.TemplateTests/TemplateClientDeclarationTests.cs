using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using McServerManager.Models.Participants;
using McServerManager.Models.Templates;
using McServerManager.Services.Participants;
using McServerManager.Services.Templates;

namespace McServerManager.TemplateTests;

public sealed class TemplateClientDeclarationTests
{
    private static readonly TemplateManifestService Service = new();

    [Fact]
    public void ExistingManifestsRemainValidWithoutAClientDeclaration()
    {
        foreach (var runtime in new[] { "vanilla", "paper", "fabric", "forge" })
            Assert.Null(Accept(Bytes(Manifest(runtime))).Manifest!.ClientDefinition);

        var fixtures = Directory.GetFiles(Path.Combine(AppContext.BaseDirectory, "Fixtures"), "*.maipilot-template.json");
        Assert.NotEmpty(fixtures);
        foreach (var path in fixtures)
            Assert.Null(Accept(File.ReadAllBytes(path)).Manifest!.ClientDefinition);
    }

    [Theory]
    [InlineData("vanilla", "fabric")]
    [InlineData("paper", "fabric")]
    [InlineData("fabric", "fabric")]
    [InlineData("forge", "fabric")]
    [InlineData("vanilla", "forge")]
    [InlineData("paper", "forge")]
    [InlineData("fabric", "forge")]
    [InlineData("forge", "forge")]
    public void ExplicitLoaderIsIndependentOfServerFamilyAndPins(string server, string client)
    {
        var definition = Client();
        definition["runtime"]!["loader"] = client;
        definition["runtime"]!["loaderVersion"] = client == "fabric" ? "0.16.9" : "52.0.2";
        var manifest = Accept(Embed(definition.ToJsonString(), Manifest(server))).Manifest!;
        Assert.Equal(server, manifest.Runtime.Type);
        Assert.Equal(client, manifest.ClientDefinition!.Runtime.Loader);
        Assert.Equal(ParticipantDefinitionReadiness.InputValidatedOnly, manifest.ClientDefinition.Readiness);
    }

    [Fact]
    public void RequiresOnlyExactMinecraftVersionConsistency()
    {
        var definition = Client();
        definition["runtime"]!["minecraftVersion"] = "1.21.2";
        Reject(Embed(definition.ToJsonString()), "ClientMinecraftVersionMismatch", "$.clientDefinition.runtime.minecraftVersion");

        var manifest = Manifest();
        manifest["runtime"]!["minecraftVersion"] = "1.21";
        definition["runtime"]!["minecraftVersion"] = "1.21.0";
        Reject(Embed(definition.ToJsonString(), manifest), "ClientMinecraftVersionMismatch", "$.clientDefinition.runtime.minecraftVersion");
        definition["runtime"]!["minecraftVersion"] = "1.21";
        Accept(Embed(definition.ToJsonString(), manifest));
    }

    [Fact]
    public void ClientAndServerListsAreSeparateAndAbsenceNeverInfersClientMods()
    {
        var server = Manifest("fabric");
        server["addons"] = new JsonArray(ServerAddon());
        Assert.Null(Accept(Bytes(server)).Manifest!.ClientDefinition);

        var manifest = Accept(Embed(Client().ToJsonString(), server)).Manifest!;
        Assert.Equal("server-only", Assert.Single(manifest.Addons).EntryId);
        Assert.Equal("Clnt1234", Assert.Single(manifest.ClientDefinition!.Mods).ProjectId);

        var emptyClient = Client();
        emptyClient["mods"] = new JsonArray();
        manifest = Accept(Embed(emptyClient.ToJsonString(), server)).Manifest!;
        Assert.Single(manifest.Addons);
        Assert.Empty(manifest.ClientDefinition!.Mods);
    }

    [Theory]
    [InlineData("null")]
    [InlineData("[]")]
    [InlineData("1")]
    [InlineData("true")]
    [InlineData("\"client\"")]
    public void PresentDeclarationMustBeAnObject(string value) =>
        Reject(Embed(value), "InvalidType", "$.clientDefinition");

    [Fact]
    public void RejectsClientServerDefinitionConfusion()
    {
        Reject(Embed(Manifest().ToJsonString()), "UnknownProperty", "$.clientDefinition");
        Reject(Bytes(Client()), "UnknownProperty", "$");
        var definition = Client();
        definition["definitionKind"] = "server";
        Reject(Embed(definition.ToJsonString()), "UnsupportedValue", "$.clientDefinition.definitionKind");
        definition = Client();
        definition["addons"] = new JsonArray();
        Reject(Embed(definition.ToJsonString()), "UnknownProperty", "$.clientDefinition");
    }

    [Theory]
    [InlineData("root", "downloadUrl")]
    [InlineData("root", "verified")]
    [InlineData("runtime", "type")]
    [InlineData("runtime", "installerVersion")]
    [InlineData("mod", "kind")]
    [InlineData("mod", "sha256")]
    [InlineData("mod", "downloadUrl")]
    public void RetainsStrictParticipantAllowLists(string target, string property)
    {
        var definition = Client();
        var node = target switch
        {
            "runtime" => definition["runtime"]!.AsObject(),
            "mod" => definition["mods"]![0]!.AsObject(),
            _ => definition
        };
        node[property] = "not permitted";
        Reject(Embed(definition.ToJsonString()), "UnknownProperty",
            target == "runtime" ? "$.clientDefinition.runtime" : target == "mod" ? "$.clientDefinition.mods[0]" : "$.clientDefinition");
    }

    [Theory]
    [InlineData("\"schemaVersion\":\"1.0\"", "$.clientDefinition")]
    [InlineData("\"minecraftVersion\":\"1.21.1\"", "$.clientDefinition.runtime")]
    [InlineData("\"projectId\":\"Clnt1234\"", "$.clientDefinition.mods[0]")]
    public void RejectsNestedDuplicateProperties(string property, string path)
    {
        var text = Client().ToJsonString().Replace(property, property + "," + property, StringComparison.Ordinal);
        Reject(Embed(text), "DuplicateProperty", path);
    }

    [Fact]
    public void RejectsDuplicateDeclarationsAndEscapedDuplicateKeys()
    {
        var raw = Encoding.UTF8.GetString(Embed(Client().ToJsonString()));
        Reject(Encoding.UTF8.GetBytes(raw[..^1] + ",\"clientDefinition\":" + Client().ToJsonString() + "}"), "DuplicateProperty", "$");
        Reject(Encoding.UTF8.GetBytes(raw[..^1] + ",\"client\\u0044efinition\":" + Client().ToJsonString() + "}"), "DuplicateProperty", "$");
        var definition = Client().ToJsonString().Replace("\"name\":\"Client fixture\"",
            "\"name\":\"Client fixture\",\"na\\u006de\":\"Client fixture\"", StringComparison.Ordinal);
        Reject(Embed(definition), "DuplicateProperty", "$.clientDefinition");
    }

    [Theory]
    [InlineData("minecraftVersion")]
    [InlineData("loaderVersion")]
    [InlineData("javaVersion")]
    public void RejectsMissingNullAndLooseRuntimePins(string property)
    {
        var definition = Client();
        definition["runtime"]!.AsObject().Remove(property);
        Reject(Embed(definition.ToJsonString()), "MissingProperty", "$.clientDefinition.runtime." + property);
        definition = Client();
        definition["runtime"]![property] = null;
        Reject(Embed(definition.ToJsonString()), "InvalidType", "$.clientDefinition.runtime." + property);
        foreach (var pin in new[] { "latest", "1.*", ">=1.2.3", "../1.2.3", "1.2.3\n" })
        {
            definition = Client();
            definition["runtime"]![property] = pin;
            Reject(Embed(definition.ToJsonString()), "InvalidPin", "$.clientDefinition.runtime." + property);
        }
    }

    [Theory]
    [InlineData("paper")]
    [InlineData("vanilla")]
    [InlineData("quilt")]
    [InlineData("neoforge")]
    public void RejectsUnsupportedClientLoaders(string loader)
    {
        var definition = Client();
        definition["runtime"]!["loader"] = loader;
        Reject(Embed(definition.ToJsonString()), "UnsupportedValue", "$.clientDefinition.runtime.loader");
    }

    [Fact]
    public void RetainsParticipantModIdVersionSideAndCountRestrictions()
    {
        foreach (var property in new[] { "projectId", "versionId" })
        {
            var definition = Client();
            definition["mods"]![0]![property] = "../not-a-pin";
            Reject(Embed(definition.ToJsonString()), "InvalidPin", "$.clientDefinition.mods[0]." + property);
        }
        foreach (var version in new[] { "latest", "recommended", "1.x", "1.*" })
        {
            var definition = Client();
            definition["mods"]![0]!["version"] = version;
            Reject(Embed(definition.ToJsonString()), version == "1.*" ? "InvalidPin" : "UnpinnedVersion", "$.clientDefinition.mods[0].version");
        }
        var invalid = Client();
        invalid["mods"]![0]!["clientSide"] = "unsupported";
        Reject(Embed(invalid.ToJsonString()), "UnsupportedValue", "$.clientDefinition.mods[0].clientSide");
        invalid = Client();
        invalid["mods"]!.AsArray().Add(invalid["mods"]![0]!.DeepClone());
        Reject(Embed(invalid.ToJsonString()), "DuplicateMod", "$.clientDefinition.mods[1]");
        invalid = Client();
        var mod = invalid["mods"]![0]!.DeepClone();
        invalid["mods"] = new JsonArray(Enumerable.Range(0, ParticipantDefinitionPolicy.MaxMods + 1).Select(_ => mod.DeepClone()).ToArray());
        Reject(Embed(invalid.ToJsonString()), "TooManyMods", "$.clientDefinition.mods");
    }

    [Fact]
    public void PreservesExactSubtreeAndWholeManifestHashesAfterCallerMutation()
    {
        var compact = Client().ToJsonString();
        var raw = compact.Replace("\"name\":\"Client fixture\"", "\"na\\u006de\" : \"参加者\\u0020fixture\"", StringComparison.Ordinal)
            .Replace(",", ",\r\n  ", StringComparison.Ordinal);
        var input = Embed(raw);
        var originalHash = Hash(input);
        var result = Accept(input);
        var definition = result.Manifest!.ClientDefinition!;
        Assert.Equal(originalHash, result.ManifestSha256);
        Assert.Equal(Hash(Encoding.UTF8.GetBytes(raw)), definition.InputSha256);
        Assert.Equal("参加者 fixture", definition.Name);
        Assert.NotEqual(result.ManifestSha256, definition.InputSha256);
        Array.Fill(input, (byte)0);
        Assert.Equal(originalHash, result.ManifestSha256);
        Assert.Equal(Hash(Encoding.UTF8.GetBytes(raw)), definition.InputSha256);
        Assert.Equal("参加者 fixture", definition.Name);

        var changedOuter = Accept(Embed(raw).Concat(new byte[] { 32 }).ToArray());
        Assert.NotEqual(result.ManifestSha256, changedOuter.ManifestSha256);
        Assert.Equal(definition.InputSha256, changedOuter.Manifest!.ClientDefinition!.InputSha256);
        var changedInner = Accept(Embed(raw.Insert(1, " ")));
        Assert.NotEqual(definition.InputSha256, changedInner.Manifest!.ClientDefinition!.InputSha256);
    }

    [Fact]
    public void AcceptsEscapedDeclarationPropertyNameWithoutChangingSubtreeHash()
    {
        var client = Client().ToJsonString();
        var bytes = Encoding.UTF8.GetBytes(Encoding.UTF8.GetString(Embed(client))
            .Replace("\"clientDefinition\"", "\"client\\u0044efinition\"", StringComparison.Ordinal));
        Assert.Equal(Hash(Encoding.UTF8.GetBytes(client)), Accept(bytes).Manifest!.ClientDefinition!.InputSha256);
    }

    [Fact]
    public void EmbeddedDefinitionRemainsOpaqueAndImmutable()
    {
        var definition = Accept(Embed(Client().ToJsonString())).Manifest!.ClientDefinition!;
        Assert.Equal(ParticipantDefinitionReadiness.InputValidatedOnly, definition.Readiness);
        Assert.Throws<NotSupportedException>(() => ((IList<ParticipantClientMod>)definition.Mods).Clear());
        foreach (var type in new[] { typeof(ParticipantClientDefinition), typeof(ParticipantClientRuntime), typeof(ParticipantClientMod) })
        {
            Assert.True(type.IsSealed);
            Assert.Empty(type.GetConstructors(BindingFlags.Public | BindingFlags.Instance));
            Assert.All(type.GetProperties(), property => Assert.False(property.CanWrite));
        }
    }

    [Fact]
    public void EnforcesExactEmbeddedUtf8ByteLimitIndependentlyOfManifestLimit()
    {
        var raw = Client().ToJsonString().Replace("Client fixture", "参加者 fixture", StringComparison.Ordinal);
        var exact = raw.Insert(raw.Length - 1, new string(' ', ParticipantDefinitionPolicy.MaxDefinitionBytes - Encoding.UTF8.GetByteCount(raw)));
        Assert.Equal(ParticipantDefinitionPolicy.MaxDefinitionBytes, Encoding.UTF8.GetByteCount(exact));
        Assert.Equal(Hash(Encoding.UTF8.GetBytes(exact)), Accept(Embed(exact)).Manifest!.ClientDefinition!.InputSha256);
        Reject(Embed(exact.Insert(1, " ")), "DefinitionTooLarge", "$.clientDefinition");

        var manifest = Embed(raw);
        var maxManifest = manifest.Concat(Enumerable.Repeat((byte)32, TemplatePolicy.MaxManifestBytes - manifest.Length)).ToArray();
        Accept(maxManifest);
        Reject(maxManifest.Concat(new byte[] { 32 }).ToArray(), "ManifestTooLarge", "$");
    }

    [Fact]
    public void RejectsInvalidUtf8EscapesAndMalformedNestedJson()
    {
        var raw = Client().ToJsonString();
        Reject(Embed(raw[..^1]), "InvalidJson", "$");
        Reject(Embed(raw.Insert(1, "/* comment */")), "InvalidJson", "$");
        Reject(Embed(raw.Replace("Client fixture", "\\uD800", StringComparison.Ordinal)), "InvalidUnicode", "$.clientDefinition");
        var bytes = Embed(raw);
        bytes[Array.IndexOf(bytes, (byte)'C')] = 0xff;
        Reject(bytes, "InvalidUtf8", "$");
    }

    [Fact]
    public async Task StreamParsingRetainsBothOriginalHashes()
    {
        var raw = Client().ToJsonString();
        var bytes = Embed(raw);
        using var stream = new MemoryStream(bytes);
        var result = await Service.ReadAsync(stream);
        Assert.True(result.IsValid);
        Assert.Equal(Hash(bytes), result.ManifestSha256);
        Assert.Equal(Hash(Encoding.UTF8.GetBytes(raw)), result.Manifest!.ClientDefinition!.InputSha256);
        Assert.True(stream.CanRead);
    }

    [Fact]
    public void LegacyStrictRootAllowListRejectsRatherThanIgnoresNewField()
    {
        // The schema 1.0 root allow-list used before client declarations was optionality-free.
        string[] legacyKeys = ["schemaVersion", "templateId", "revision", "name", "description", "edition", "runtime", "settings", "addons"];
        using var document = JsonDocument.Parse(Embed(Client().ToJsonString()));
        var exception = Assert.Throws<TemplateManifestException>(() =>
            TemplateJsonFields.Object(document.RootElement, "$", legacyKeys, legacyKeys));
        Assert.Equal("UnknownProperty", exception.Code);
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
            ["name"] = "Fixture", ["description"] = "Synthetic offline fixture", ["edition"] = "java",
            ["runtime"] = spec, ["settings"] = new JsonObject(), ["addons"] = new JsonArray()
        };
    }

    private static JsonObject Client() => new()
    {
        ["schemaVersion"] = "1.0", ["definitionKind"] = "client", ["name"] = "Client fixture",
        ["runtime"] = new JsonObject
        {
            ["minecraftVersion"] = "1.21.1", ["loader"] = "fabric", ["loaderVersion"] = "0.16.9", ["javaVersion"] = "21.0.2"
        },
        ["mods"] = new JsonArray(new JsonObject
        {
            ["provider"] = "modrinth", ["projectId"] = "Clnt1234", ["versionId"] = "Vers5678",
            ["name"] = "Synthetic MOD", ["version"] = "1.2.3", ["clientSide"] = "required"
        })
    };

    private static JsonObject ServerAddon() => new()
    {
        ["entryId"] = "server-only", ["kind"] = "mod", ["selection"] = "direct", ["sha256"] = new string('a', 64),
        ["sizeBytes"] = 1, ["requires"] = new JsonArray(),
        ["source"] = new JsonObject
        {
            ["provider"] = "modrinth", ["projectId"] = "Serv1234", ["versionId"] = "Serv5678",
            ["fileName"] = "server-only.jar", ["sha512"] = new string('a', 128)
        }
    };

    private static byte[] Embed(string client, JsonObject? manifest = null) =>
        Encoding.UTF8.GetBytes((manifest ?? Manifest()).ToJsonString()[..^1] + ",\"clientDefinition\": \n" + client + "\n}");

    private static byte[] Bytes(JsonNode value) => Encoding.UTF8.GetBytes(value.ToJsonString());
    private static string Hash(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();

    private static TemplateValidationResult Accept(byte[] input)
    {
        var result = Service.Parse(input);
        Assert.True(result.IsValid, string.Join(", ", result.Issues));
        Assert.NotNull(result.Manifest);
        Assert.Empty(result.Issues);
        return result;
    }

    private static void Reject(byte[] input, string code, string path)
    {
        var result = Service.Parse(input);
        Assert.False(result.IsValid);
        Assert.Null(result.Manifest);
        Assert.Null(result.ManifestSha256);
        var issue = Assert.Single(result.Issues);
        Assert.Equal(code, issue.Code);
        Assert.Equal(path, issue.Path);
    }
}
