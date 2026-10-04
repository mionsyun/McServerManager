using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using McServerManager.Models.Authoring;
using McServerManager.Models.Editions;
using McServerManager.Models.Templates;
using McServerManager.Services.Authoring;
using McServerManager.Services.Editions;
using McServerManager.Services.Participants;
using McServerManager.Services.Templates;

namespace McServerManager.TemplateTests.Authoring;

public sealed class TemplateAuthoringServiceTests
{
    [Fact]
    public void FreeDirectCallsCannotCreateEditPrepareOrExportButCanStillImport()
    {
        var parser = new TemplateManifestService();
        var free = new TemplateAuthoringService(new EditionPolicy(AppEdition.Free), parser);
        var input = Manifest();
        Assert.True(parser.Parse(input).IsValid);
        Assert.Throws<InvalidOperationException>(() => free.CreateNew());
        Assert.Throws<InvalidOperationException>(() => free.OpenForEdit(input));
        var pro = Service();
        var opened = pro.CreateNew();
        var draft = ValidDraft();
        var prepared = Prepared(pro, opened.Session!, draft);
        Assert.Throws<InvalidOperationException>(() => free.PrepareExport(opened.Session!, draft));
        Assert.Throws<InvalidOperationException>(() => free.Export(opened.Session!, draft, prepared));
    }

    [Theory]
    [InlineData(false, EditionCapability.TemplateCreate)]
    [InlineData(false, EditionCapability.TemplateExport)]
    [InlineData(true, EditionCapability.TemplateEdit)]
    [InlineData(true, EditionCapability.TemplateExport)]
    public void PrepareAndExportRecheckOperationAndExportCapabilities(bool edit, EditionCapability denied)
    {
        var policy = new SwitchablePolicy();
        var service = new TemplateAuthoringService(policy, new TemplateManifestService());
        var opened = edit ? service.OpenForEdit(Manifest()) : service.CreateNew();
        var draft = edit ? opened.Draft! : ValidDraft();
        var token = Prepared(service, opened.Session!, draft);
        policy.Denied = denied;
        Assert.Throws<InvalidOperationException>(() => service.PrepareExport(opened.Session!, draft));
        Assert.Throws<InvalidOperationException>(() => service.Export(opened.Session!, draft, token));
    }

    [Theory]
    [InlineData("vanilla")]
    [InlineData("paper")]
    [InlineData("fabric")]
    [InlineData("forge")]
    public void CreatesPinnedExplicitTemplateWithNewIdentityAndEmptyServerAddons(string family)
    {
        var service = Service();
        var opened = service.CreateNew();
        Assert.True(opened.IsValid);
        Assert.Empty(opened.Issues);
        Assert.False(opened.Session!.IsEditing);
        Assert.False(opened.Session.IsServerRuntimeLocked);
        Assert.Null(opened.Session.ImportedManifestSha256);
        Assert.Equal(0, opened.Session.BaseRevision);
        Assert.Equal(1, opened.Session.Revision);
        Assert.NotEqual(Guid.Empty, opened.Session.TemplateId);
        Assert.NotEqual(opened.Session.TemplateId, service.CreateNew().Session!.TemplateId);
        Assert.Equal("", opened.Draft!.Runtime.MinecraftVersion);
        Assert.False(service.PrepareExport(opened.Session, opened.Draft).IsValid);
        var draft = ValidDraft(family);
        var token = Prepared(service, opened.Session, draft);
        var bytes = service.Export(opened.Session, draft, token);
        var result = new TemplateManifestService().Parse(bytes);
        Assert.True(result.IsValid);
        Assert.Equal(opened.Session.TemplateId, result.Manifest!.TemplateId);
        Assert.Equal(1, result.Manifest.Revision);
        Assert.Equal("1.0", result.Manifest.SchemaVersion);
        Assert.Equal("java", result.Manifest.Edition);
        Assert.Equal(family, result.Manifest.Runtime.Type);
        Assert.Empty(result.Manifest.Addons);
        Assert.Null(result.Manifest.ClientDefinition);
        Assert.Equal(Hash(bytes), token.ManifestSha256);
        Assert.Null(token.ClientDefinitionSha256);
        Assert.Equal(bytes.Length, token.ByteCount);
        Assert.True(token.InputValidatedOnly);
    }

    [Fact]
    public void EditPreservesEveryAddonFieldAndHashWhileIncrementingRevision()
    {
        var service = Service();
        var input = Manifest(withAddons: true);
        var original = new TemplateManifestService().Parse(input).Manifest!;
        var opened = service.OpenForEdit(input);
        Assert.True(opened.IsValid);
        Assert.Equal(Hash(input), opened.Session!.ImportedManifestSha256);
        Assert.Equal(original.TemplateId, opened.Session.TemplateId);
        Assert.Equal(7, opened.Session.BaseRevision);
        Assert.Equal(8, opened.Session.Revision);
        Assert.True(opened.Session.IsEditing);
        Assert.True(opened.Session.IsServerRuntimeLocked);
        Assert.Equal(2, opened.Session.PreservedServerAddonCount);
        Assert.Throws<NotSupportedException>(() => ((IList<TemplateAddon>)opened.Session.PreservedServerAddons).Clear());
        Assert.Throws<NotSupportedException>(() => ((IList<string>)opened.Session.PreservedServerAddons[0].Requires).Clear());
        Array.Fill(input, (byte)0);
        var draft = opened.Draft! with { Name = "Edited", Settings = new() { Pvp = false, MaxPlayers = 12 } };
        var token = Prepared(service, opened.Session, draft);
        var exported = new TemplateManifestService().Parse(service.Export(opened.Session, draft, token)).Manifest!;
        Assert.Equal(8, exported.Revision);
        Assert.Equal(original.TemplateId, exported.TemplateId);
        Assert.Equal("Edited", exported.Name);
        Assert.False(exported.Settings.Pvp);
        Assert.Equal(12, exported.Settings.MaxPlayers);
        Assert.Null(exported.ClientDefinition);
        for (var index = 0; index < original.Addons.Count; index++)
        {
            var before = original.Addons[index];
            var after = exported.Addons[index];
            Assert.Equal(before.EntryId, after.EntryId);
            Assert.Equal(before.Kind, after.Kind);
            Assert.Equal(before.Selection, after.Selection);
            Assert.Equal(before.Source, after.Source);
            Assert.Equal(before.Sha256, after.Sha256);
            Assert.Equal(before.SizeBytes, after.SizeBytes);
            Assert.Equal(before.Requires, after.Requires);
        }
    }

    [Fact]
    public void ImportedAddonRuntimeCannotBeRetargetedButIndependentExplicitClientCanBeEdited()
    {
        var service = Service();
        var opened = service.OpenForEdit(Manifest(withAddons: true));
        var draft = opened.Draft!;
        foreach (var changed in new[]
        {
            draft.Runtime with { MinecraftVersion = "1.21.2" },
            draft.Runtime with { LoaderVersion = "0.16.10" },
            draft.Runtime with { InstallerVersion = "1.0.2" },
            Runtime("forge"), draft.Runtime with { Build = "123" }
        })
            Reject(service.PrepareExport(opened.Session!, draft with { Runtime = changed }), "ServerRuntimeLocked", "$.runtime");
        var explicitClient = draft with { ClientDefinition = Client() with { Loader = "forge", LoaderVersion = "52.0.2" } };
        Assert.NotNull(Prepared(service, opened.Session!, explicitClient));
        var empty = service.OpenForEdit(Manifest());
        Assert.False(empty.Session!.IsServerRuntimeLocked);
        Assert.NotNull(Prepared(service, empty.Session, empty.Draft! with { Runtime = Runtime("paper") }));
    }

    [Fact]
    public void ExplicitClientRowsRoundTripIndependentlyAndHashTheExactOutputSubtree()
    {
        var service = Service();
        var opened = service.CreateNew();
        var draft = ValidDraft() with { ClientDefinition = Client() };
        var token = Prepared(service, opened.Session!, draft);
        var output = service.Export(opened.Session!, draft, token);
        using var document = JsonDocument.Parse(output);
        var clientBytes = Encoding.UTF8.GetBytes(document.RootElement.GetProperty("clientDefinition").GetRawText());
        Assert.Equal(Hash(clientBytes), token.ClientDefinitionSha256);
        var imported = service.OpenForEdit(output);
        Assert.Equal(draft.ClientDefinition.Name, imported.Draft!.ClientDefinition!.Name);
        Assert.Equal(draft.ClientDefinition.MinecraftVersion, imported.Draft.ClientDefinition.MinecraftVersion);
        Assert.Equal(draft.ClientDefinition.Loader, imported.Draft.ClientDefinition.Loader);
        Assert.Equal(draft.ClientDefinition.LoaderVersion, imported.Draft.ClientDefinition.LoaderVersion);
        Assert.Equal(draft.ClientDefinition.JavaVersion, imported.Draft.ClientDefinition.JavaVersion);
        Assert.Equal(draft.ClientDefinition.Mods, imported.Draft.ClientDefinition.Mods);
        Assert.Empty(imported.Session!.PreservedServerAddons);
        var noClient = imported.Draft with { ClientDefinition = null };
        Assert.Null(Prepared(service, imported.Session, noClient).ClientDefinitionSha256);
    }

    [Fact]
    public void StrictValidationRejectsRuntimePinsSettingsAndClientDeclarations()
    {
        var service = Service();
        var session = service.CreateNew().Session!;
        var draft = ValidDraft();
        Reject(service.PrepareExport(session, draft with { Runtime = Runtime("paper") with { Build = "latest" } }), "InvalidValue", "$.runtime.build");
        Reject(service.PrepareExport(session, draft with { Runtime = Runtime("fabric") with { InstallerVersion = null } }), "MissingRuntimePin", "$.runtime.installerVersion");
        Reject(service.PrepareExport(session, draft with { Runtime = draft.Runtime with { Build = "12" } }), "InvalidRuntimePin", "$.runtime.build");
        Reject(service.PrepareExport(session, draft with { Settings = new() { MaxPlayers = 1001 } }), "InvalidValue", "$.settings.maxPlayers");
        Reject(service.PrepareExport(session, draft with { ClientDefinition = Client() with { MinecraftVersion = "1.21.2" } }), "ClientMinecraftVersionMismatch", "$.clientDefinition.runtime.minecraftVersion");
        Reject(service.PrepareExport(session, draft with { ClientDefinition = Client() with { JavaVersion = "latest" } }), "InvalidPin", "$.clientDefinition.runtime.javaVersion");
        var row = Client().Mods[0];
        foreach (var invalid in new[] { row with { ProjectId = "../evil" }, row with { Version = "1.*" } })
            Assert.False(service.PrepareExport(session, draft with { ClientDefinition = Client() with { Mods = new[] { invalid } } }).IsValid);
        Reject(service.PrepareExport(session, draft with { ClientDefinition = Client() with { Mods = new[] { row with { Version = "latest" } } } }), "UnpinnedVersion", "$.clientDefinition.mods[0].version");
        Reject(service.PrepareExport(session, draft with { ClientDefinition = Client() with { Mods = new[] { row, row } } }), "DuplicateMod", "$.clientDefinition.mods[1]");
        Reject(service.PrepareExport(session, draft with { ClientDefinition = Client() with { Mods = new[] { row with { Provider = "curseforge" } } } }), "UnsupportedValue", "$.clientDefinition.mods[0].provider");
        Reject(service.PrepareExport(session, draft with { Name = "\uD800" }), "InvalidUnicode", "$");
        Assert.False(service.PrepareExport(session, draft with { Runtime = null! }).IsValid);
        Assert.False(service.PrepareExport(session, draft with { Settings = null! }).IsValid);
        Assert.False(service.PrepareExport(session, draft with { ClientDefinition = Client() with { Mods = null! } }).IsValid);
    }

    [Theory]
    [InlineData("downloadUrl")]
    [InlineData("world")]
    [InlineData("jvmArgs")]
    [InlineData("trusted")]
    public void EditStrictlyRejectsUnknownFieldsRatherThanDroppingThem(string property)
    {
        var node = JsonNode.Parse(Manifest())!.AsObject();
        node[property] = "not supported";
        var opened = Service().OpenForEdit(Encoding.UTF8.GetBytes(node.ToJsonString()));
        Assert.False(opened.IsValid);
        Assert.Null(opened.Session);
        Assert.Null(opened.Draft);
        Assert.Equal("UnknownProperty", Assert.Single(opened.Issues).Code);
    }

    [Fact]
    public void EditRejectsInvalidAddonHashAndRevisionOverflow()
    {
        var node = JsonNode.Parse(Manifest(withAddons: true))!.AsObject();
        node["addons"]![0]!["sha256"] = new string('A', 64);
        Assert.False(Service().OpenForEdit(Encoding.UTF8.GetBytes(node.ToJsonString())).IsValid);
        node = JsonNode.Parse(Manifest())!.AsObject();
        node["revision"] = int.MaxValue;
        var opened = Service().OpenForEdit(Encoding.UTF8.GetBytes(node.ToJsonString()));
        Assert.False(opened.IsValid);
        Assert.Null(opened.Session);
        Assert.Equal("RevisionOverflow", Assert.Single(opened.Issues).Code);
        node["revision"] = int.MaxValue - 1;
        Assert.Equal(int.MaxValue, Service().OpenForEdit(Encoding.UTF8.GetBytes(node.ToJsonString())).Session!.Revision);
    }

    [Fact]
    public void BoundedImportAndSerializationEnforceBothManifestAndClientLimits()
    {
        var service = Service();
        var bytes = Manifest();
        var exact = bytes.Concat(Enumerable.Repeat((byte)32, TemplatePolicy.MaxManifestBytes - bytes.Length)).ToArray();
        Assert.True(service.OpenForEdit(exact).IsValid);
        var oversized = service.OpenForEdit(exact.Concat(new byte[] { 32 }).ToArray());
        Assert.Equal("ManifestTooLarge", Assert.Single(oversized.Issues).Code);
        var session = service.CreateNew().Session!;
        Reject(service.PrepareExport(session, ValidDraft() with { Description = new string('a', TemplatePolicy.MaxManifestBytes + 1) }), "ManifestTooLarge", "$");
        Reject(service.PrepareExport(session, ValidDraft() with { ClientDefinition = Client() with
        { Mods = Enumerable.Repeat(Client().Mods[0], ParticipantDefinitionPolicy.MaxMods + 1).ToArray() } }), "TooManyMods", "$.clientDefinition.mods");
        var misleadingRows = new MisleadingRowList(Client().Mods[0]);
        Reject(service.PrepareExport(session, ValidDraft() with { ClientDefinition = Client() with { Mods = misleadingRows } }), "TooManyMods", "$.clientDefinition.mods");
        Assert.Equal(ParticipantDefinitionPolicy.MaxMods + 1, misleadingRows.Enumerated);
        var rows = Enumerable.Range(0, 256).Select(index => Client().Mods[0] with
        {
            ProjectId = $"P{index:D7}", VersionId = $"V{index:D7}", Name = new string('参', 120), Note = new string('参', 240)
        }).ToArray();
        Reject(service.PrepareExport(session, ValidDraft() with { ClientDefinition = Client() with { Mods = rows } }), "DefinitionTooLarge", "$.clientDefinition");
    }

    [Fact]
    public void ExportRejectsStaleNestedRowsOtherDraftFieldsAndCrossSessionReceipts()
    {
        var service = Service();
        var session = service.CreateNew().Session!;
        var rows = new List<TemplateAuthoringClientModDraft> { Client().Mods[0] };
        var draft = ValidDraft() with { ClientDefinition = Client() with { Mods = rows } };
        var token = Prepared(service, session, draft);
        rows[0] = rows[0] with { Note = "Changed" };
        Assert.Throws<InvalidOperationException>(() => service.Export(session, draft, token));
        token = Prepared(service, session, draft);
        Assert.Throws<InvalidOperationException>(() => service.Export(session, draft with { Name = "Changed" }, token));
        Assert.Throws<InvalidOperationException>(() => service.Export(session, draft with { Settings = new() { Pvp = false } }, token));
        Assert.Throws<InvalidOperationException>(() => service.Export(session, draft with { ClientDefinition = null }, token));
        Assert.Throws<InvalidOperationException>(() => service.Export(service.CreateNew().Session!, draft, token));
        Assert.Throws<InvalidOperationException>(() => Service().Export(session, draft, token));
        rows.Clear();
        Assert.Throws<InvalidOperationException>(() => service.Export(session, draft, token));
    }

    [Fact]
    public void ExportReceiptAndSessionAreOpaqueAndReturnedBytesAreDefensiveCopies()
    {
        var service = Service();
        var session = service.CreateNew().Session!;
        var draft = ValidDraft();
        var token = Prepared(service, session, draft);
        var first = service.Export(session, draft, token);
        Array.Fill(first, (byte)0);
        var second = service.Export(session, draft, token);
        Assert.Equal(token.ManifestSha256, Hash(second));
        Assert.True(new TemplateManifestService().Parse(second).IsValid);
        foreach (var type in new[] { typeof(TemplateAuthoringSession), typeof(TemplateAuthoringPreparedExport) })
        {
            Assert.True(type.IsSealed);
            Assert.Empty(type.GetConstructors(BindingFlags.Public | BindingFlags.Instance));
            Assert.All(type.GetProperties(), property => Assert.False(property.CanWrite));
            Assert.DoesNotContain(type.GetProperties(), property => property.PropertyType == typeof(byte[]));
        }
    }

    [Fact]
    public void CancellationCreatesNoReceiptOrOutputAndDoesNotCorruptExistingSession()
    {
        var service = Service();
        var session = service.CreateNew().Session!;
        var draft = ValidDraft();
        var token = Prepared(service, session, draft);
        using var source = new CancellationTokenSource();
        source.Cancel();
        Assert.Throws<OperationCanceledException>(() => service.CreateNew(source.Token));
        Assert.Throws<OperationCanceledException>(() => service.OpenForEdit(Manifest(), source.Token));
        Assert.Throws<OperationCanceledException>(() => service.PrepareExport(session, draft, source.Token));
        Assert.Throws<OperationCanceledException>(() => service.Export(session, draft, token, source.Token));
        Assert.Equal(token.ManifestSha256, Hash(service.Export(session, draft, token)));
    }

    private static TemplateAuthoringService Service() => new(new EditionPolicy(AppEdition.Pro), new TemplateManifestService());
    private static TemplateAuthoringDraft ValidDraft(string family = "vanilla") => new()
    { Name = "Author fixture", Description = "Offline declarations only", Runtime = Runtime(family), Settings = new() };
    private static TemplateRuntime Runtime(string family) => new()
    {
        Type = family, MinecraftVersion = "1.21.1", Build = family == "paper" ? "123" : family == "forge" ? "52.0.1" : null,
        LoaderVersion = family == "fabric" ? "0.16.9" : null, InstallerVersion = family == "fabric" ? "1.0.1" : null
    };
    private static TemplateAuthoringClientDraft Client() => new()
    {
        Name = "Client fixture", MinecraftVersion = "1.21.1", Loader = "fabric", LoaderVersion = "0.16.9", JavaVersion = "21.0.2",
        Mods = new[] { new TemplateAuthoringClientModDraft { ProjectId = "Clnt1234", VersionId = "Vers5678", Name = "Synthetic MOD",
            Version = "1.2.3", ClientSide = "optional", Note = "Explicit note" } }
    };

    private static byte[] Manifest(bool withAddons = false)
    {
        var runtime = new JsonObject { ["type"] = withAddons ? "fabric" : "vanilla", ["minecraftVersion"] = "1.21.1" };
        if (withAddons) { runtime["loaderVersion"] = "0.16.9"; runtime["installerVersion"] = "1.0.1"; }
        var addons = new JsonArray();
        if (withAddons)
        {
            addons.Add(Addon("main", "direct", 'a', "dep"));
            addons.Add(Addon("dep", "required-dependency", 'b'));
        }
        return Encoding.UTF8.GetBytes(new JsonObject
        {
            ["schemaVersion"] = "1.0", ["templateId"] = "7b4643cb-6e70-4a4f-bd3d-dbf6f3b9a1bc", ["revision"] = 7,
            ["name"] = "Fixture", ["description"] = "Synthetic offline fixture", ["edition"] = "java",
            ["runtime"] = runtime, ["settings"] = new JsonObject { ["gamemode"] = "creative" }, ["addons"] = addons
        }.ToJsonString());
    }
    private static JsonObject Addon(string id, string selection, char hash, params string[] requires) => new()
    {
        ["entryId"] = id, ["kind"] = "mod", ["selection"] = selection, ["sha256"] = new string(hash, 64), ["sizeBytes"] = 1024,
        ["requires"] = new JsonArray(requires.Select(value => (JsonNode?)JsonValue.Create(value)).ToArray()),
        ["source"] = new JsonObject { ["provider"] = "modrinth", ["projectId"] = "Project" + id, ["versionId"] = "Version" + id,
            ["fileName"] = id + ".jar", ["sha512"] = new string(hash, 128) }
    };
    private static TemplateAuthoringPreparedExport Prepared(TemplateAuthoringService service, TemplateAuthoringSession session, TemplateAuthoringDraft draft)
    {
        var result = service.PrepareExport(session, draft);
        Assert.True(result.IsValid, string.Join(", ", result.Issues));
        return Assert.IsType<TemplateAuthoringPreparedExport>(result.PreparedExport);
    }
    private static void Reject(TemplateAuthoringValidationResult result, string code, string path)
    {
        Assert.False(result.IsValid);
        Assert.Null(result.PreparedExport);
        var issue = Assert.Single(result.Issues);
        Assert.Equal(code, issue.Code);
        Assert.Equal(path, issue.Path);
    }
    private static string Hash(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
    private sealed class MisleadingRowList(TemplateAuthoringClientModDraft row) : IReadOnlyList<TemplateAuthoringClientModDraft>
    {
        public int Count => 1;
        public int Enumerated { get; private set; }
        public TemplateAuthoringClientModDraft this[int index] => row;
        public IEnumerator<TemplateAuthoringClientModDraft> GetEnumerator()
        {
            while (true) { Enumerated++; yield return row; }
        }
        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();
    }
    private sealed class SwitchablePolicy : IEditionPolicy
    {
        public AppEdition Edition => AppEdition.Pro;
        public string DisplayName => "Test policy";
        public EditionCapability? Denied { get; set; }
        public bool Allows(EditionCapability capability) => capability != Denied;
    }
}
