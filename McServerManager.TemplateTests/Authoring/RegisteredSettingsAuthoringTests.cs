using System.Text;
using System.Text.Json;
using McServerManager.Models;
using McServerManager.Models.Authoring;
using McServerManager.Models.Editions;
using McServerManager.Services.Authoring;
using McServerManager.Services.AuthoringFiles;
using McServerManager.Services.Editions;
using McServerManager.Services.Templates;
using McServerManager.ViewModels.Authoring;

namespace McServerManager.TemplateTests.Authoring;

public sealed class RegisteredSettingsAuthoringTests
{
    private static readonly EditionPolicy Pro = new(AppEdition.Pro);
    private static TemplateAuthoringService Service() => new(Pro, new TemplateManifestService());
    private static TemplateAuthoringViewModel ViewModel() => new(Service(), Pro);

    private static ServerConfig Config()
    {
        var config = new ServerConfig
        {
            ServerId = Guid.NewGuid().ToString("N"), Type = "Vanilla", Version = "1.21.1", Name = "登録設定の試験",
            Difficulty = "hard", GameMode = "creative", MaxPlayers = 12, ViewDistance = 8, Pvp = false, SpawnProtection = 7,
            DirectoryPath = "DO-NOT-COPY-DIRECTORY", JavaPath = "DO-NOT-COPY-JAVA",
            JavaExtraArguments = "DO-NOT-COPY-ARGS", Motd = "DO-NOT-COPY-MOTD",
            WorldName = "DO-NOT-COPY-WORLD", Seed = "DO-NOT-COPY-SEED", Port = 29999
        };
        config.CreationRuntimeDeclaration = ServerCreationRuntimeDeclarationPolicy.Create(config.ServerId, config.Type, config.Version);
        return config;
    }
    private static RegisteredServerTemplateSource Source() => RegisteredServerTemplateSourceFactory.Create(Config());

    [Fact]
    public void ProjectionIsImmutableAllowlistAndExportContainsNoRegistrationIdentityOrSecrets()
    {
        var config = Config();
        var source = RegisteredServerTemplateSourceFactory.Create(config);
        config.Name = "changed"; config.Version = "1.20.1"; config.Difficulty = "peaceful";
        config.CreationRuntimeDeclaration = null;
        var service = Service();
        var opened = service.CreateFromRegisteredSettings(source);
        Assert.True(opened.IsValid);
        Assert.Equal("登録設定の試験", opened.Draft!.Name);
        Assert.Equal("1.21.1", opened.Draft.Runtime.MinecraftVersion);
        Assert.Equal("hard", opened.Draft.Settings.Difficulty);
        Assert.Null(opened.Draft.Settings.SimulationDistance);
        Assert.Null(opened.Draft.ClientDefinition);
        Assert.Empty(opened.Session!.PreservedServerAddons);
        Assert.False(opened.Session.IsEditing);
        Assert.Equal(1, opened.Session.Revision);
        Assert.NotEqual(Guid.Parse(source.ServerId), opened.Session.TemplateId);
        var prepared = service.PrepareExport(opened.Session, opened.Draft);
        Assert.True(prepared.IsValid);
        var bytes = service.Export(opened.Session, opened.Draft, prepared.PreparedExport!);
        var json = Encoding.UTF8.GetString(bytes);
        Assert.DoesNotContain("DO-NOT-COPY", json);
        Assert.DoesNotContain(source.ServerId, json);
        Assert.DoesNotContain("29999", json);
        using var document = JsonDocument.Parse(bytes);
        Assert.Equal(new[] { "difficulty", "gamemode", "maxPlayers", "pvp", "spawnProtection", "viewDistance" },
            document.RootElement.GetProperty("settings").EnumerateObject().Select(x => x.Name).OrderBy(x => x, StringComparer.Ordinal));
        Assert.False(document.RootElement.TryGetProperty("CreationRuntimeDeclaration", out _));
        Assert.Empty(document.RootElement.GetProperty("addons").EnumerateArray());
    }

    [Theory]
    [InlineData("missing")] [InlineData("version")] [InlineData("server")] [InlineData("type")]
    [InlineData("schema")] [InlineData("latest")] [InlineData("snapshot")] [InlineData("duplicate")]
    public void UnknownOrChangedCreationDeclarationFailsWithoutUsableSession(string mutation)
    {
        var source = Source();
        var bad = mutation switch
        {
            "missing" => source with { CreationDeclaration = null },
            "version" => source with { MinecraftVersion = "1.20.1" },
            "server" => source with { CreationDeclaration = source.CreationDeclaration! with { ServerId = Guid.NewGuid().ToString("N") } },
            "type" => source with { ServerType = "Fabric" },
            "schema" => source with { CreationDeclaration = source.CreationDeclaration! with { SchemaVersion = 2 } },
            "latest" => source with { MinecraftVersion = "latest" },
            "snapshot" => source with { MinecraftVersion = "24w33a" },
            _ => source with { ServerId = Guid.NewGuid().ToString("N") }
        };
        var result = Service().CreateFromRegisteredSettings(bad);
        Assert.False(result.IsValid);
        Assert.Null(result.Session); Assert.Null(result.Draft);
        Assert.Contains(result.Issues, x => x.Code == "CreationDeclarationUnavailable");
        var vm = ViewModel();
        Assert.False(vm.CreateFromRegisteredSettings(bad));
        Assert.Contains("手動", vm.Summary);
        Assert.Contains("作成記録", vm.Details);
        Assert.False(vm.HasDraft);
    }

    [Theory]
    [InlineData("name")] [InlineData("difficulty")] [InlineData("gamemode")]
    [InlineData("players")] [InlineData("distance")] [InlineData("spawn")]
    [InlineData("null-difficulty")] [InlineData("null-gamemode")]
    public void InvalidRegisteredFieldsAreNotSilentlyDroppedOrClamped(string mutation)
    {
        var source = Source();
        var bad = mutation switch
        {
            "name" => source with { Name = new string('x', 81) },
            "difficulty" => source with { Difficulty = "unknown" },
            "gamemode" => source with { GameMode = "unknown" },
            "players" => source with { MaxPlayers = -1 },
            "distance" => source with { ViewDistance = int.MaxValue },
            "spawn" => source with { SpawnProtection = -1 },
            "null-difficulty" => source with { Difficulty = null! },
            _ => source with { GameMode = null! }
        };
        var result = Service().CreateFromRegisteredSettings(bad);
        Assert.False(result.IsValid); Assert.Null(result.Session); Assert.Null(result.Draft);
    }

    [Fact]
    public void FreeDirectCallsAndPreCancellationNeverCreateAUsableDraft()
    {
        var freePolicy = new EditionPolicy(AppEdition.Free);
        var free = new TemplateAuthoringService(freePolicy, new TemplateManifestService());
        Assert.Throws<InvalidOperationException>(() => free.CreateFromRegisteredSettings(Source()));
        var vm = new TemplateAuthoringViewModel(free, freePolicy);
        Assert.False(vm.CreateFromRegisteredSettings(Source()));
        Assert.False(vm.HasDraft); Assert.False(vm.CanExport);
        using var cancelled = new CancellationTokenSource(); cancelled.Cancel();
        Assert.Throws<OperationCanceledException>(() => Service().CreateFromRegisteredSettings(Source(), cancelled.Token));
        Assert.Throws<ArgumentNullException>(() => Service().CreateFromRegisteredSettings(null!));
        Assert.Throws<ArgumentNullException>(() => RegisteredServerTemplateSourceFactory.Create(null!));
    }

    [Fact]
    public void SeedIsDirtyAndMustBeReviewedAndAcknowledgedBeforeExport()
    {
        var vm = ViewModel();
        Assert.True(vm.CreateFromRegisteredSettings(Source()));
        Assert.True(vm.IsDirty); Assert.False(vm.CanExport); Assert.False(vm.AcknowledgedLimitations);
        Assert.Contains("登録設定", vm.DraftSourceDescription);
        Assert.True(vm.ValidatePreview()); Assert.False(vm.CanExport);
        vm.AcknowledgedLimitations = true;
        Assert.NotNull(vm.CreateExportBytes());
        var revision = vm.ReviewRevision;
        vm.ServerRuntime.MinecraftVersion = "1.20.1";
        Assert.False(vm.CanExport); Assert.False(vm.IsCurrentExport(revision));
        Assert.True(vm.ValidatePreview());
        Assert.Contains("入力開始時の作成記録: Vanilla 1.21.1", vm.Details);
        Assert.Contains("Minecraft 1.20.1", vm.Details);
        Assert.Contains("未検証", vm.Details);
        Assert.DoesNotContain("入力元: フォームで新規作成", vm.Details);
    }

    [Fact]
    public void DirtySeedReplacementRequiresDiscardAndFailedReplacementKeepsOriginalButInvalidatesApproval()
    {
        var vm = ViewModel();
        Assert.True(vm.CreateFromRegisteredSettings(Source()));
        var first = vm.Name;
        Assert.False(vm.CreateNew()); Assert.Equal(first, vm.Name);
        Assert.False(vm.CreateFromRegisteredSettings(Source() with { Name = "new" }));
        Assert.Equal(first, vm.Name);
        Assert.True(vm.ValidatePreview()); vm.AcknowledgedLimitations = true;
        Assert.NotNull(vm.CreateExportBytes());
        Assert.False(vm.CreateFromRegisteredSettings(Source() with { CreationDeclaration = null }, discardChanges: true));
        Assert.Equal(first, vm.Name); Assert.True(vm.IsDirty); Assert.False(vm.CanExport);
        Assert.Contains("登録設定", vm.DraftSourceDescription);
        Assert.True(vm.CreateNew(discardChanges: true));
        Assert.Equal("フォームで新規作成", vm.DraftSourceDescription);
    }

    [Fact]
    public async Task RegisteredDraftCanEditSaveAndReopenAsNextRevisionUsingRealFileService()
    {
        var root = Path.Combine(Path.GetTempPath(), "registered-template-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var vm = ViewModel();
            Assert.True(vm.CreateFromRegisteredSettings(Source()));
            vm.Name = "編集後の名前";
            Assert.True(vm.ValidatePreview()); vm.AcknowledgedLimitations = true;
            var revision = vm.ReviewRevision;
            var bytes = vm.CreateExportBytes()!;
            var path = Path.Combine(root, "registered.maipilot-template.json");
            await new TemplateAuthoringFileService(Pro).SaveNewTemplateAsync(path, bytes);
            vm.ReportSaved(path, revision);
            Assert.False(vm.IsDirty); Assert.False(vm.CanExport);
            Assert.Equal("テンプレートファイルを読み込み", vm.DraftSourceDescription);
            vm.Description = "次の版";
            Assert.True(vm.ValidatePreview()); vm.AcknowledgedLimitations = true;
            var next = new TemplateManifestService().Parse(vm.CreateExportBytes()!).Manifest!;
            var initial = new TemplateManifestService().Parse(await File.ReadAllBytesAsync(path)).Manifest!;
            Assert.Equal(initial.TemplateId, next.TemplateId); Assert.Equal(2, next.Revision);
            Assert.Equal("編集後の名前", initial.Name); Assert.Equal(1, initial.Revision);
            Assert.Empty(initial.Addons); Assert.Null(initial.ClientDefinition);
            Assert.Equal(bytes, await File.ReadAllBytesAsync(path));
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Fact]
    public void ResetClearsRegisteredSourceLabelAndRequiresDiscardForSeed()
    {
        var vm = ViewModel();
        Assert.True(vm.CreateFromRegisteredSettings(Source()));
        Assert.False(vm.Reset()); Assert.True(vm.HasDraft);
        Assert.True(vm.Reset(discardChanges: true));
        Assert.False(vm.HasDraft); Assert.False(vm.IsDirty); Assert.False(vm.CanExport);
        Assert.Equal("フォームで新規作成", vm.DraftSourceDescription);
    }
}
