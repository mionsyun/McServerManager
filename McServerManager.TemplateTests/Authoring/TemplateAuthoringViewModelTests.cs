using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using McServerManager.Models.Authoring;
using McServerManager.Models.Editions;
using McServerManager.Services.Authoring;
using McServerManager.Services.Editions;
using McServerManager.Services.Templates;
using McServerManager.ViewModels.Authoring;

namespace McServerManager.TemplateTests.Authoring;

public sealed class TemplateAuthoringViewModelTests
{
    [Fact]
    public void FreeDirectEntryPointsNeverReachAuthoringService()
    {
        var service = new TrackingService();
        var vm = new TemplateAuthoringViewModel(service, new EditionPolicy(AppEdition.Free));
        Assert.False(vm.CreateNew());
        Assert.False(vm.LoadForEdit(Fixture(), "example.json"));
        Assert.False(vm.ValidatePreview());
        vm.AcknowledgedLimitations = true;
        Assert.Null(vm.CreateExportBytes());
        Assert.False(vm.AddClientMod());
        vm.Name = "bypass";
        vm.ServerRuntime.MinecraftVersion = "1.21.1";
        Assert.False(vm.HasDraft);
        Assert.False(vm.CanCreate);
        Assert.False(vm.CanLoad);
        Assert.False(vm.CanExport);
        Assert.False(vm.AcknowledgedLimitations);
        Assert.Empty(vm.Name);
        Assert.Empty(vm.ServerRuntime.MinecraftVersion);
        Assert.Equal(0, service.Calls);
    }

    [Fact]
    public void NewDraftHasNoGuessedVersionsClientListOrServerAddons()
    {
        var vm = NewVm();
        Assert.True(vm.CreateNew());
        Assert.True(vm.HasDraft);
        Assert.False(vm.IsDirty);
        Assert.Empty(vm.ServerRuntime.MinecraftVersion);
        Assert.Empty(vm.ServerRuntime.Build);
        Assert.Empty(vm.ServerRuntime.LoaderVersion);
        Assert.Empty(vm.ClientRuntime.MinecraftVersion);
        Assert.Empty(vm.ClientRuntime.LoaderVersion);
        Assert.Empty(vm.ClientRuntime.JavaVersion);
        Assert.False(vm.HasClientDefinition);
        Assert.Empty(vm.ClientMods);
        Assert.Contains("サーバーMODの追加は今回未対応", vm.ServerAddonsSummary);
        Assert.Null(vm.OriginalManifestSha256);
        Assert.False(vm.ValidatePreview());
        Assert.Null(vm.CreateExportBytes());
    }

    [Fact]
    public void ValidPreviewRequiresExplicitAcknowledgmentAndReturnsStrictTemplate()
    {
        var vm = ValidNew();
        vm.AcknowledgedLimitations = true;
        Assert.False(vm.AcknowledgedLimitations);
        Assert.True(vm.ValidatePreview());
        Assert.True(vm.IsReviewing);
        Assert.True(vm.CanAcknowledge);
        Assert.False(vm.CanExport);
        Assert.Null(vm.CreateExportBytes());
        vm.AcknowledgedLimitations = true;
        Assert.True(vm.CanExport);
        var output = Assert.IsType<byte[]>(vm.CreateExportBytes());
        var parsed = new TemplateManifestService().Parse(output);
        Assert.True(parsed.IsValid);
        Assert.Equal("友達用テンプレート", parsed.Manifest!.Name);
        Assert.Empty(parsed.Manifest.Addons);
        Assert.Null(parsed.Manifest.ClientDefinition);
        Assert.Equal(1, parsed.Manifest.Revision);
        Assert.Equal(parsed.ManifestSha256, vm.OutputManifestSha256);
        Assert.True(vm.IsDirty);
        Assert.Contains("保存はまだ完了していません", vm.Summary);
        Assert.True(vm.IsCurrentExport(vm.ReviewRevision));
    }

    [Fact]
    public void ReviewSeparatesProvenanceOutputAndUnverifiedClientDeclaration()
    {
        var vm = NewVm();
        var source = Fixture(client: true);
        Assert.True(vm.LoadForEdit(source, @"C:\private\source.json"));
        var inputHash = new TemplateManifestService().Parse(source).ManifestSha256;
        Assert.Equal("source.json", vm.SourceFileName);
        Assert.Equal(inputHash, vm.OriginalManifestSha256);
        vm.Name = "編集後";
        Assert.True(vm.ValidatePreview());
        foreach (var expected in new[] { "入力形式のみ", "申告値", "取得元", "依存 MOD", "動作互換性は未検証", "サーバー適用・自動インストールはしません",
            "編集元 SHA-256", "新しい出力 SHA-256", "参加者用の構成", "Minecraft 1.21.1", "fabric 0.16.9", "Java 21.0.2",
            "必須 MOD", "任意 MOD", "AbCd1234", "EfGh5678", "IjKl1234", "MnOp5678", "メモ" })
            Assert.Contains(expected, vm.Details);
        Assert.DoesNotContain(@"C:\private", vm.Details);
        Assert.NotEqual(vm.OriginalManifestSha256, vm.OutputManifestSha256);
    }

    [Fact]
    public void EmptyExplicitClientListWarnsWithoutInferringServerMods()
    {
        var vm = NewVm();
        Assert.True(vm.LoadForEdit(Fixture(addons: true), "server.json"));
        EnableClient(vm);
        Assert.Empty(vm.ClientMods);
        Assert.True(vm.ValidatePreview());
        Assert.Contains("参加者用 MOD は指定されていません", vm.Details);
        Assert.Contains("すべて揃うかは未検証", vm.Details);
        Assert.Contains("サーバーの MOD 一覧から参加者用 MOD を推測していません", vm.Details);
        Assert.False(vm.CanExport);
    }

    [Theory]
    [InlineData("name")]
    [InlineData("description")]
    [InlineData("runtime")]
    [InlineData("settings")]
    [InlineData("clientName")]
    [InlineData("clientRuntime")]
    [InlineData("clientToggle")]
    [InlineData("row")]
    [InlineData("add")]
    [InlineData("remove")]
    public void EveryInputMutationInvalidatesPreviewAcknowledgmentAndProducedBytes(string field)
    {
        var vm = NewVm();
        Assert.True(vm.LoadForEdit(Fixture(client: true), "source.json"));
        ReviewAndExport(vm);
        var revision = vm.ReviewRevision;
        switch (field)
        {
            case "name": vm.Name = "変更"; break;
            case "description": vm.Description = "変更"; break;
            case "runtime": vm.ServerRuntime.MinecraftVersion = "1.21.2"; break;
            case "settings": vm.Settings.MaxPlayers = "12"; break;
            case "clientName": vm.ClientName = "変更"; break;
            case "clientRuntime": vm.ClientRuntime.JavaVersion = "21.0.3"; break;
            case "clientToggle": vm.HasClientDefinition = false; break;
            case "row": vm.ClientMods[0].Note = "変更"; break;
            case "add": Assert.True(vm.AddClientMod()); break;
            case "remove": Assert.True(vm.RemoveClientMod(vm.ClientMods[0])); break;
        }
        Assert.True(vm.IsDirty);
        Assert.True(vm.ReviewRevision > revision);
        Assert.False(vm.IsReviewing);
        Assert.False(vm.AcknowledgedLimitations);
        Assert.False(vm.CanExport);
        Assert.False(vm.IsCurrentExport(revision));
        Assert.Empty(vm.Details);
        Assert.Null(vm.OutputManifestSha256);
        Assert.Null(vm.CreateExportBytes());
    }

    [Fact]
    public void RemovedRowAndReplacedSectionsCannotChangeCurrentDraft()
    {
        var vm = NewVm();
        Assert.True(vm.LoadForEdit(Fixture(client: true), "source.json"));
        var removed = vm.ClientMods[0];
        Assert.True(vm.RemoveClientMod(removed));
        ReviewAndExport(vm);
        var revision = vm.ReviewRevision;
        removed.Note = "detached";
        Assert.Equal(revision, vm.ReviewRevision);
        Assert.True(vm.CanExport);
        var oldRuntime = vm.ServerRuntime;
        var oldSettings = vm.Settings;
        Assert.True(vm.CreateNew(discardChanges: true));
        revision = vm.ReviewRevision;
        oldRuntime.MinecraftVersion = "1.21.9";
        oldSettings.MaxPlayers = "99";
        Assert.Equal(revision, vm.ReviewRevision);
        Assert.False(vm.IsDirty);
        Assert.Empty(vm.ServerRuntime.MinecraftVersion);
    }

    [Fact]
    public void ImportedAddonsLockAllServerPinsAndRemainReadOnlyOnOutput()
    {
        var source = Fixture(addons: true);
        var vm = NewVm();
        Assert.True(vm.LoadForEdit(source, "mods.json"));
        Assert.True(vm.IsServerRuntimeLocked);
        Assert.True(vm.ServerRuntime.IsLocked);
        Assert.False(vm.CanEditServerRuntime);
        var revision = vm.ReviewRevision;
        vm.ServerRuntime.Type = "vanilla";
        vm.ServerRuntime.MinecraftVersion = "1.21.2";
        vm.ServerRuntime.Build = "999";
        vm.ServerRuntime.LoaderVersion = "9.9.9";
        vm.ServerRuntime.InstallerVersion = "9.9.9";
        Assert.Equal("fabric", vm.ServerRuntime.Type);
        Assert.Equal("1.21.1", vm.ServerRuntime.MinecraftVersion);
        Assert.Empty(vm.ServerRuntime.Build);
        Assert.Equal("0.16.9", vm.ServerRuntime.LoaderVersion);
        Assert.Equal("1.0.1", vm.ServerRuntime.InstallerVersion);
        Assert.Equal(revision, vm.ReviewRevision);
        Assert.False(vm.IsDirty);
        Assert.Contains("server-mod", vm.ServerAddonsSummary);
        Assert.Contains("読み取り専用", vm.ServerAddonsSummary);
        vm.Name = "名前のみ変更";
        var bytes = ReviewAndExport(vm);
        Assert.True(JsonNode.DeepEquals(JsonNode.Parse(source)!["addons"], JsonNode.Parse(bytes)!["addons"]));
        Assert.False(vm.HasClientDefinition);
        Assert.Empty(vm.ClientMods);
    }

    [Theory]
    [InlineData("-1")]
    [InlineData("1.5")]
    [InlineData("abc")]
    [InlineData(" 12")]
    [InlineData("2147483648")]
    public void InvalidNumberTextNeverSilentlyBecomesAnOmittedSetting(string input)
    {
        var vm = ValidNew();
        vm.Settings.MaxPlayers = input;
        Assert.False(vm.ValidatePreview());
        Assert.False(vm.CanAcknowledge);
        Assert.Contains("整数", vm.Summary);
        Assert.Equal(input, vm.Settings.MaxPlayers);
        Assert.Null(vm.CreateExportBytes());
    }

    [Fact]
    public void RangePinAndClientVersionErrorsRetainEditableInput()
    {
        var vm = ValidNew();
        vm.Settings.ViewDistance = "33";
        Assert.False(vm.ValidatePreview());
        Assert.Equal("33", vm.Settings.ViewDistance);
        vm.Settings.ViewDistance = "8";
        vm.ServerRuntime.Type = "paper";
        Assert.False(vm.ValidatePreview());
        vm.ServerRuntime.Build = "123";
        EnableClient(vm);
        vm.ClientRuntime.MinecraftVersion = "1.20.1";
        Assert.False(vm.ValidatePreview());
        Assert.Contains("ClientMinecraftVersionMismatch", vm.Details);
        vm.ClientRuntime.MinecraftVersion = "1.21.1";
        Assert.True(vm.ValidatePreview());
    }

    [Fact]
    public void ClientRowsAreBoundedAndDuplicateRowsCannotBeExported()
    {
        var vm = ValidNew();
        EnableClient(vm);
        for (var index = 0; index < 256; index++) Assert.True(vm.AddClientMod());
        Assert.False(vm.CanAddClientMod);
        Assert.False(vm.AddClientMod());
        Assert.Equal(256, vm.ClientMods.Count);
        Assert.True(vm.RemoveClientMod(vm.ClientMods[0]));
        Assert.True(vm.CanAddClientMod);
        Assert.True(vm.LoadForEdit(Fixture(client: true), "source.json", discardChanges: true));
        vm.ClientMods[1].ProjectId = vm.ClientMods[0].ProjectId;
        Assert.False(vm.ValidatePreview());
        Assert.Contains("DuplicateMod", vm.Details);
    }

    [Fact]
    public void DirtyReplacementRequiresConfirmationAndFailedLoadPreservesContent()
    {
        var vm = ValidNew();
        var name = vm.Name;
        Assert.False(vm.CreateNew());
        Assert.False(vm.Reset());
        Assert.False(vm.LoadForEdit(Fixture(), "source.json"));
        Assert.Equal(name, vm.Name);
        Assert.True(vm.IsDirty);
        Assert.False(vm.LoadForEdit(Encoding.UTF8.GetBytes("{}"), "bad.json", discardChanges: true));
        Assert.Equal(name, vm.Name);
        Assert.True(vm.IsDirty);
        Assert.True(vm.HasDraft);
        Assert.False(vm.CanExport);
        Assert.True(vm.LoadForEdit(Fixture(), "source.json", discardChanges: true));
        Assert.False(vm.IsDirty);
        Assert.Equal("元のテンプレート", vm.Name);
    }

    [Theory]
    [InlineData("back")]
    [InlineData("cancel")]
    [InlineData("openFailure")]
    public void BackCancelAndOpenFailurePreserveDraftButRequireFreshReview(string action)
    {
        var vm = ValidNew();
        ReviewAndExport(vm);
        if (action == "back") vm.BackToEditor();
        else if (action == "cancel") vm.Cancel();
        else vm.ReportOpenFailure();
        Assert.Equal("友達用テンプレート", vm.Name);
        Assert.True(vm.IsDirty);
        Assert.True(vm.HasDraft);
        Assert.False(vm.IsReviewing);
        Assert.False(vm.CanExport);
        Assert.False(vm.AcknowledgedLimitations);
        Assert.Null(vm.CreateExportBytes());
        Assert.True(vm.ValidatePreview());
    }

    [Fact]
    public void CanceledPickerPreservesCurrentReviewAndAcknowledgment()
    {
        var vm = ValidNew();
        ReviewAndExport(vm);
        var revision = vm.ReviewRevision;
        var details = vm.Details;
        vm.ReportOpenCanceled();
        Assert.Equal(details, vm.Details);
        Assert.Equal(revision, vm.ReviewRevision);
        Assert.True(vm.CanExport);
        Assert.True(vm.IsDirty);
    }

    [Fact]
    public void SuccessfulSaveRebasesExactPrivateBytesAndNextSaveIncrementsRevision()
    {
        var vm = ValidNew();
        var first = ReviewAndExport(vm);
        var firstManifest = new TemplateManifestService().Parse(first).Manifest!;
        var hash = vm.OutputManifestSha256;
        Array.Fill(first, (byte)0); // Caller cannot corrupt the saved-byte identity held by the VM.
        vm.ReportSaved("first.json", vm.ReviewRevision);
        Assert.False(vm.IsDirty);
        Assert.False(vm.CanExport);
        Assert.False(vm.AcknowledgedLimitations);
        Assert.Equal(hash, vm.OriginalManifestSha256);
        Assert.Equal("first.json", vm.SourceFileName);
        vm.Description = "次の版";
        var second = ReviewAndExport(vm);
        var next = new TemplateManifestService().Parse(second).Manifest!;
        Assert.Equal(firstManifest.TemplateId, next.TemplateId);
        Assert.Equal(firstManifest.Revision + 1, next.Revision);
    }

    [Fact]
    public void FailedOrCanceledSaveNeverRebasesAndStaleReportsCannotMarkNewEditsClean()
    {
        var vm = ValidNew();
        var first = ReviewAndExport(vm);
        var revision = vm.ReviewRevision;
        vm.ReportSaveCanceled(revision);
        Assert.Contains("未保存", vm.Summary);
        vm.ReportSaveFailure(revision);
        Assert.Contains("未保存", vm.Summary);
        Assert.True(vm.IsDirty);
        Assert.Equal(first, vm.CreateExportBytes());
        vm.Description = "まだ保存していない変更";
        var summary = vm.Summary;
        vm.ReportSaved("stale.json", revision);
        vm.ReportSaveCanceled(revision);
        vm.ReportSaveFailure(revision);
        Assert.True(vm.IsDirty);
        Assert.Equal(summary, vm.Summary);
        Assert.Null(vm.OriginalManifestSha256);
        Assert.Equal(1, new TemplateManifestService().Parse(ReviewAndExport(vm)).Manifest!.Revision);
    }

    [Fact]
    public void SavePickerCanceledBeforeByteCreationReportsUnsavedAndKeepsReview()
    {
        var vm = ValidNew();
        Assert.True(vm.ValidatePreview());
        vm.AcknowledgedLimitations = true;
        var revision = vm.ReviewRevision;
        Assert.False(vm.IsCurrentExport(revision));
        vm.ReportSaveCanceled(revision);
        Assert.Contains("保存を取り消しました", vm.Summary);
        Assert.True(vm.CanExport);
        Assert.True(vm.IsDirty);
        vm.ReportSaveFailure(revision);
        Assert.Contains("保存できませんでした", vm.Summary);
        Assert.True(vm.CanExport);
    }

    [Fact]
    public void ConfirmedDiskCommitStillReportsSavedAfterCapabilityRevocation()
    {
        var policy = new MutablePolicy();
        var vm = NewVm(policy);
        Assert.True(vm.CreateNew());
        vm.Name = "保存済み";
        vm.ServerRuntime.MinecraftVersion = "1.21.1";
        ReviewAndExport(vm);
        var revision = vm.ReviewRevision;
        policy.Allowed = false;
        vm.ReportSaved("/chosen/folder/saved.json", revision);
        Assert.Contains("保存しました", vm.Summary);
        Assert.Contains("/chosen/folder/saved.json", vm.Summary);
        Assert.DoesNotContain("まだ完了していません", vm.Summary);
        Assert.False(vm.IsDirty);
        Assert.False(vm.CanExport);
        Assert.False(vm.CanEdit);
    }

    [Fact]
    public void CapabilityRevocationBlocksDirectMutationAndAllExportPaths()
    {
        var policy = new MutablePolicy();
        var vm = NewVm(policy);
        Assert.True(vm.LoadForEdit(Fixture(client: true), "source.json"));
        ReviewAndExport(vm);
        var name = vm.Name;
        var rowName = vm.ClientMods[0].Name;
        var version = vm.ServerRuntime.MinecraftVersion;
        policy.Allowed = false;
        vm.Name = "bypass";
        vm.ClientMods[0].Name = "bypass";
        vm.ServerRuntime.MinecraftVersion = "bypass";
        vm.Settings.MaxPlayers = "99";
        Assert.Equal(name, vm.Name);
        Assert.Equal(rowName, vm.ClientMods[0].Name);
        Assert.Equal(version, vm.ServerRuntime.MinecraftVersion);
        Assert.Empty(vm.Settings.MaxPlayers);
        Assert.False(vm.CanExport);
        Assert.Null(vm.CreateExportBytes());
        Assert.False(vm.ValidatePreview());
        Assert.False(vm.CreateNew(discardChanges: true));
        Assert.False(vm.LoadForEdit(Fixture(), "new.json", discardChanges: true));
        Assert.False(vm.AcknowledgedLimitations);
    }

    [Fact]
    public void ResetClearsProvenanceAndRemovedInputCannotReappear()
    {
        var vm = NewVm();
        Assert.True(vm.LoadForEdit(Fixture(client: true, addons: true), "source.json"));
        vm.Name = "変更";
        Assert.True(vm.Reset(discardChanges: true));
        Assert.False(vm.HasDraft);
        Assert.False(vm.IsDirty);
        Assert.Null(vm.OriginalManifestSha256);
        Assert.Empty(vm.SourceFileName);
        Assert.Empty(vm.ClientMods);
        Assert.False(vm.IsServerRuntimeLocked);
        Assert.True(vm.CreateNew());
        Assert.False(vm.HasClientDefinition);
        Assert.Empty(vm.ClientMods);
    }

    [Fact]
    public void FormReviewAndAcknowledgmentNotifyDerivedBindings()
    {
        var vm = NewVm();
        var changes = new List<string?>();
        vm.PropertyChanged += (_, args) => changes.Add(args.PropertyName);
        vm.CreateNew();
        vm.Name = "名前";
        vm.ServerRuntime.MinecraftVersion = "1.21.1";
        ReviewAndExport(vm);
        foreach (var property in new[] { "HasDraft", "Name", "IsDirty", "ServerRuntime", "CanEdit", "CanEditServerRuntime", "CanValidate",
            "CanAcknowledge", "CanExport", "IsReviewing", "AcknowledgedLimitations", "OutputManifestSha256", "ReviewRevision", "Details", "Summary" })
            Assert.Contains(property, changes);
    }

    private static TemplateAuthoringViewModel NewVm(IEditionPolicy? policy = null)
    {
        policy ??= new EditionPolicy(AppEdition.Pro);
        return new(new TemplateAuthoringService(policy, new TemplateManifestService()), policy);
    }
    private static TemplateAuthoringViewModel ValidNew()
    {
        var vm = NewVm();
        Assert.True(vm.CreateNew());
        vm.Name = "友達用テンプレート";
        vm.ServerRuntime.MinecraftVersion = "1.21.1";
        return vm;
    }
    private static byte[] ReviewAndExport(TemplateAuthoringViewModel vm)
    {
        Assert.True(vm.ValidatePreview(), vm.Details);
        vm.AcknowledgedLimitations = true;
        return Assert.IsType<byte[]>(vm.CreateExportBytes());
    }
    private static void EnableClient(TemplateAuthoringViewModel vm)
    {
        vm.HasClientDefinition = true;
        vm.ClientName = "参加者用";
        vm.ClientRuntime.MinecraftVersion = "1.21.1";
        vm.ClientRuntime.Loader = "fabric";
        vm.ClientRuntime.LoaderVersion = "0.16.9";
        vm.ClientRuntime.JavaVersion = "21.0.2";
    }
    private static byte[] Fixture(bool client = false, bool addons = false)
    {
        var root = JsonSerializer.SerializeToNode(new
        {
            schemaVersion = "1.0", templateId = "7b4643cb-6e70-4a4f-bd3d-dbf6f3b9a1bc", revision = 3,
            name = "元のテンプレート", description = "説明", edition = "java",
            runtime = new { type = "fabric", minecraftVersion = "1.21.1", loaderVersion = "0.16.9", installerVersion = "1.0.1" },
            settings = new { },
            addons = addons ? new object[] { new
            {
                entryId = "server-mod", kind = "mod", selection = "direct", sha256 = new string('a', 64), sizeBytes = 1024,
                requires = Array.Empty<string>(), source = new { provider = "modrinth", projectId = "Serv1234", versionId = "Vers1234", fileName = "server.jar", sha512 = new string('b', 128) }
            } } : Array.Empty<object>()
        })!;
        if (client) root["clientDefinition"] = JsonSerializer.SerializeToNode(new
        {
            schemaVersion = "1.0", definitionKind = "client", name = "参加者用",
            runtime = new { minecraftVersion = "1.21.1", loader = "fabric", loaderVersion = "0.16.9", javaVersion = "21.0.2" },
            mods = new[] {
                new { provider = "modrinth", projectId = "AbCd1234", versionId = "EfGh5678", name = "必須 MOD", version = "1.2.3", clientSide = "required", note = "メモ" },
                new { provider = "modrinth", projectId = "IjKl1234", versionId = "MnOp5678", name = "任意 MOD", version = "2.0.0", clientSide = "optional", note = "メモ" }
            }
        });
        return Encoding.UTF8.GetBytes(root.ToJsonString());
    }
    private sealed class MutablePolicy : IEditionPolicy
    {
        public bool Allowed { get; set; } = true;
        public AppEdition Edition => Allowed ? AppEdition.Pro : AppEdition.Free;
        public string DisplayName => "Test";
        public bool Allows(EditionCapability capability) => Allowed;
    }
    private sealed class TrackingService : ITemplateAuthoringService
    {
        public int Calls { get; private set; }
        public TemplateAuthoringOpenResult CreateNew(CancellationToken cancellationToken = default) { Calls++; throw new InvalidOperationException(); }
        public TemplateAuthoringOpenResult OpenForEdit(ReadOnlyMemory<byte> bytes, CancellationToken cancellationToken = default) { Calls++; throw new InvalidOperationException(); }
        public TemplateAuthoringValidationResult PrepareExport(TemplateAuthoringSession session, TemplateAuthoringDraft draft, CancellationToken cancellationToken = default) { Calls++; throw new InvalidOperationException(); }
        public byte[] Export(TemplateAuthoringSession session, TemplateAuthoringDraft draft, TemplateAuthoringPreparedExport prepared, CancellationToken cancellationToken = default) { Calls++; throw new InvalidOperationException(); }
    }
}
