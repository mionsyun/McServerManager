using System.Security.Cryptography;
using System.Text;
using McServerManager.Models.Editions;
using McServerManager.Models.Participants;
using McServerManager.Models.Templates;
using McServerManager.Services.Editions;
using McServerManager.Services.Participants;
using McServerManager.Services.Templates;
using McServerManager.ViewModels;

namespace McServerManager.TemplateTests.Participants;

/// <summary>Offline synthetic declarations only; no published preset or provider verification.</summary>
public sealed class TemplateParticipantReviewTests
{
    [Theory]
    [InlineData(AppEdition.Free)]
    [InlineData(AppEdition.Pro)]
    public async Task ValidTemplateBridgesSameOpaqueDefinitionWithoutReparseAndRequiresAcknowledgment(AppEdition edition)
    {
        var inspection = await Inspect();
        var zip = new CapturingZip();
        var vm = Export(zip: zip, edition: edition);
        var notifications = new List<string?>();
        vm.PropertyChanged += (_, change) => notifications.Add(change.PropertyName);
        vm.LoadValidatedDefinition(inspection.ClientDefinition!, inspection.OriginalManifestSha256!);
        foreach (var name in new[] { "OriginalManifestSha256", "DefinitionSha256", "IsTemplateDefinition" })
            Assert.Contains(name, notifications);
        Assert.True(vm.HasDefinition);
        Assert.True(vm.IsTemplateDefinition);
        Assert.False(vm.AcknowledgedLimitations);
        Assert.False(vm.CanExport);
        Assert.Null(await vm.CreateZipAsync());
        Assert.Null(zip.Definition);
        vm.AcknowledgedLimitations = true;
        Assert.NotNull(await vm.CreateZipAsync());
        Assert.Same(inspection.ClientDefinition, zip.Definition);
        Assert.Equal(ParticipantDefinitionReadiness.InputValidatedOnly, zip.Definition!.Readiness);
        Assert.Contains("入力形式のみ", vm.Details);
        Assert.Contains("未検証", inspection.ParticipantDefinitionStatus);
        Assert.Contains("サーバーの MOD 一覧から推測していません", vm.Details);
    }

    [Fact]
    public async Task BothHashesPreserveOriginalWhitespaceEscapesAndBytes()
    {
        var bytes = Template(Client);
        var expectedTemplateHash = Hash(bytes);
        var expectedClientHash = Hash(Encoding.UTF8.GetBytes(Client));
        var inspection = new TemplateInspectionViewModel(new TemplateManifestService());
        await inspection.InspectAsync(new MemoryStream(bytes));
        Array.Fill(bytes, (byte)0);
        Assert.Equal(expectedTemplateHash, inspection.OriginalManifestSha256);
        Assert.Equal(expectedClientHash, inspection.ClientDefinition!.InputSha256);
        Assert.NotEqual(expectedTemplateHash, expectedClientHash);
        Assert.Equal("Client \u30c6\u30b9\u30c8", inspection.ClientDefinition.Name);
        var vm = Export();
        vm.LoadValidatedDefinition(inspection.ClientDefinition, inspection.OriginalManifestSha256!);
        Assert.Equal(expectedTemplateHash, vm.OriginalManifestSha256);
        Assert.Equal(expectedClientHash, vm.DefinitionSha256);
        Assert.Contains($"元テンプレート SHA-256: {expectedTemplateHash}", vm.Details);
        Assert.Contains($"埋め込み定義 SHA-256: {expectedClientHash}", vm.Details);
    }

    [Fact]
    public async Task OldTemplateWithoutClientRemainsValidButCannotReviewParticipants()
    {
        var inspection = await Inspect(client: null);
        Assert.Contains("形式の確認ができました", inspection.Summary);
        Assert.Null(inspection.ClientDefinition);
        Assert.NotNull(inspection.OriginalManifestSha256);
        Assert.False(inspection.CanReviewParticipantDefinition);
        Assert.Contains("参加者用の構成定義がありません", inspection.ParticipantDefinitionStatus);
        Assert.Contains("推測せず", inspection.ParticipantDefinitionStatus);
    }

    [Theory]
    [InlineData("null")]
    [InlineData("{}")]
    [InlineData("[]")]
    public async Task MalformedClientInvalidatesWholeTemplateAndClearsPriorBridge(string client)
    {
        var inspection = await Inspect();
        await inspection.InspectAsync(new MemoryStream(Template(client)));
        AssertCleared(inspection);
        Assert.Contains("このファイルは確認できません", inspection.Summary);
        Assert.Contains("形式が不正", inspection.ParticipantDefinitionStatus);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CancelOrOpenFailureClearsCompletedBridge(bool openFailure)
    {
        var inspection = await Inspect();
        if (openFailure) inspection.ReportOpenFailure(); else inspection.Cancel();
        AssertCleared(inspection);
        Assert.False(inspection.IsBusy);
        Assert.Contains("案内は作れません", inspection.ParticipantDefinitionStatus);
    }

    [Fact]
    public async Task ReplacementImmediatelyClearsBridgeAndCanceledOldResultCannotRestoreIt()
    {
        var result = Valid();
        var pending = Completion<TemplateValidationResult>();
        var calls = 0;
        CancellationToken oldToken = default;
        var inspection = new TemplateInspectionViewModel(new StubTemplates((_, token) =>
        {
            if (++calls == 1) return Task.FromResult(result);
            oldToken = token;
            return pending.Task;
        }));
        await inspection.InspectAsync(new MemoryStream());
        Assert.True(inspection.CanReviewParticipantDefinition);
        var replacement = inspection.InspectAsync(new MemoryStream());
        AssertCleared(inspection);
        inspection.Cancel();
        var summary = inspection.Summary;
        pending.SetResult(result);
        await replacement;
        Assert.True(oldToken.IsCancellationRequested);
        AssertCleared(inspection);
        Assert.Equal(summary, inspection.Summary);
    }

    [Fact]
    public async Task StaleTemplateResultCannotReplaceNewerDefinitionOrHash()
    {
        var pending = Completion<TemplateValidationResult>();
        var newer = Valid(Client.Replace("AbCd1234", "QrSt1234"));
        var calls = 0;
        var inspection = new TemplateInspectionViewModel(new StubTemplates((_, _) =>
            ++calls == 1 ? pending.Task : Task.FromResult(newer)));
        var oldRead = inspection.InspectAsync(new MemoryStream());
        await inspection.InspectAsync(new MemoryStream());
        pending.SetResult(Valid());
        await oldRead;
        Assert.Same(newer.Manifest!.ClientDefinition, inspection.ClientDefinition);
        Assert.Equal(newer.ManifestSha256, inspection.OriginalManifestSha256);
        Assert.True(inspection.CanReviewParticipantDefinition);
    }

    [Fact]
    public async Task ExternalCancellationDiscardsSuccessfulResultAndPreCanceledReadNeverStarts()
    {
        var pending = Completion<TemplateValidationResult>();
        using var cancellation = new CancellationTokenSource();
        var calls = 0;
        var inspection = new TemplateInspectionViewModel(new StubTemplates((_, _) => { calls++; return pending.Task; }));
        var read = inspection.InspectAsync(new MemoryStream(), cancellation.Token);
        cancellation.Cancel();
        pending.SetResult(Valid());
        await read;
        AssertCleared(inspection);
        Assert.Contains("取り消し", inspection.Summary);
        await inspection.InspectAsync(new MemoryStream(), cancellation.Token);
        Assert.Equal(1, calls);
        AssertCleared(inspection);
        Assert.False(inspection.IsBusy);
    }

    [Fact]
    public async Task ManualReplacementResetsTemplateProvenanceAndAcknowledgment()
    {
        var inspection = await Inspect();
        var vm = Export(new ParticipantClientDefinitionService());
        vm.LoadValidatedDefinition(inspection.ClientDefinition!, inspection.OriginalManifestSha256!);
        vm.AcknowledgedLimitations = true;
        var revision = vm.ReviewRevision;
        await vm.LoadAsync(Encoding.UTF8.GetBytes(Client));
        Assert.True(vm.HasDefinition);
        Assert.True(vm.ReviewRevision > revision);
        Assert.False(vm.IsTemplateDefinition);
        Assert.Null(vm.OriginalManifestSha256);
        Assert.Equal(inspection.ClientDefinition!.InputSha256, vm.DefinitionSha256);
        Assert.DoesNotContain("元テンプレート SHA-256", vm.Details);
        Assert.False(vm.AcknowledgedLimitations);
        Assert.False(vm.CanExport);
        await vm.LoadAsync("{}"u8.ToArray());
        Assert.Null(vm.DefinitionSha256);
        Assert.False(vm.HasDefinition);
    }

    [Fact]
    public async Task TemplateLoadInvalidatesPendingManualReadAndRequiresFreshAcknowledgment()
    {
        var pending = Completion<ParticipantDefinitionValidationResult>();
        var vm = Export(new StubDefinitions((_, _) => pending.Task));
        var oldLoad = vm.LoadAsync(Encoding.UTF8.GetBytes(Client));
        var inspection = await Inspect();
        vm.LoadValidatedDefinition(inspection.ClientDefinition!, inspection.OriginalManifestSha256!);
        vm.AcknowledgedLimitations = true;
        pending.SetResult(new ParticipantClientDefinitionService().Parse(Encoding.UTF8.GetBytes(Client.Replace("Client", "Old"))));
        await oldLoad;
        Assert.Equal(inspection.ClientDefinition!.Name, vm.DefinitionName);
        Assert.Equal(inspection.OriginalManifestSha256, vm.OriginalManifestSha256);
        Assert.True(vm.CanExport);
        vm.LoadValidatedDefinition(inspection.ClientDefinition, inspection.OriginalManifestSha256!);
        Assert.False(vm.AcknowledgedLimitations);
        Assert.False(vm.CanExport);
        vm.Clear();
        Assert.Null(vm.OriginalManifestSha256);
        Assert.Null(vm.DefinitionSha256);
        Assert.False(vm.IsTemplateDefinition);
    }

    [Fact]
    public async Task BridgePropertiesNotifyAndOpenFailureDiscardsPendingTemplateResult()
    {
        var pending = Completion<TemplateValidationResult>();
        var inspection = new TemplateInspectionViewModel(new StubTemplates((_, _) => pending.Task));
        var notifications = new List<string?>();
        inspection.PropertyChanged += (_, change) => notifications.Add(change.PropertyName);
        var read = inspection.InspectAsync(new MemoryStream());
        inspection.ReportOpenFailure();
        pending.SetResult(Valid());
        await read;
        AssertCleared(inspection);
        Assert.Contains("開けません", inspection.Summary);
        foreach (var name in new[] { "ClientDefinition", "OriginalManifestSha256", "CanReviewParticipantDefinition", "ParticipantDefinitionStatus" })
            Assert.Contains(name, notifications);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("malformed")]
    [InlineData("ABCDEF0123456789ABCDEF0123456789ABCDEF0123456789ABCDEF0123456789")]
    public async Task MissingOrMalformedTemplateHashCannotLeaveReviewOrAcknowledgment(string? hash)
    {
        var valid = Valid();
        var inspection = new TemplateInspectionViewModel(new StubTemplates((_, _) =>
            Task.FromResult(valid with { ManifestSha256 = hash })));
        await inspection.InspectAsync(new MemoryStream());
        AssertCleared(inspection);
        Assert.Contains("SHA-256 を確認できない", inspection.ParticipantDefinitionStatus);
        var vm = Export();
        vm.LoadValidatedDefinition(valid.Manifest!.ClientDefinition!, valid.ManifestSha256!);
        vm.AcknowledgedLimitations = true;
        Assert.Throws<ArgumentException>(() => vm.LoadValidatedDefinition(valid.Manifest.ClientDefinition!, hash!));
        Assert.False(vm.HasDefinition);
        Assert.False(vm.AcknowledgedLimitations);
        Assert.Null(vm.OriginalManifestSha256);
        vm.ReportTemplateUnavailable(inspection.ParticipantDefinitionStatus);
        Assert.Equal(inspection.ParticipantDefinitionStatus, vm.Details);
        Assert.Contains("別のテンプレート", vm.Summary);
        Assert.False(vm.CanExport);
    }

    private static ParticipantExportViewModel Export(IParticipantClientDefinitionService? definitions = null,
        CapturingZip? zip = null, AppEdition edition = AppEdition.Free) => new(
        definitions ?? new StubDefinitions((_, _) => throw new InvalidOperationException("Reparse must not occur")),
        zip ?? new CapturingZip(), new EditionPolicy(edition));
    private static async Task<TemplateInspectionViewModel> Inspect(string? client = Client)
    {
        var inspection = new TemplateInspectionViewModel(new TemplateManifestService());
        await inspection.InspectAsync(new MemoryStream(Template(client)));
        return inspection;
    }
    private static void AssertCleared(TemplateInspectionViewModel inspection)
    {
        Assert.Null(inspection.ClientDefinition);
        Assert.Null(inspection.OriginalManifestSha256);
        Assert.False(inspection.CanReviewParticipantDefinition);
    }
    private static TemplateValidationResult Valid(string client = Client) => new TemplateManifestService().Parse(Template(client));
    private static TaskCompletionSource<T> Completion<T>() => new(TaskCreationOptions.RunContinuationsAsynchronously);
    private static string Hash(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
    private static byte[] Template(string? client) => Encoding.UTF8.GetBytes("""
        { "schemaVersion":"1.0", "templateId":"7b4643cb-6e70-4a4f-bd3d-dbf6f3b9a1bc", "revision":1,
          "name":"Synthetic template", "description":"Test only", "edition":"java",
          "runtime":{"type":"vanilla","minecraftVersion":"1.21.1"}, "settings":{}, "addons":[]
        """ + (client is null ? "" : ",\n  \"clientDefinition\": " + client) + "\n}\n");
    private const string Client = """
        { "schemaVersion":"1.0", "definitionKind":"client", "name":"\u0043lient テスト",
          "runtime":{"minecraftVersion":"1.21.1","loader":"fabric","loaderVersion":"0.16.9","javaVersion":"21.0.2"},
          "mods":[{"provider":"modrinth","projectId":"AbCd1234","versionId":"EfGh5678",
            "name":"Synthetic client MOD","version":"1.2.3","clientSide":"required"}] }
        """;

    private sealed class CapturingZip : IParticipantListZipService
    {
        public ParticipantClientDefinition? Definition { get; private set; }
        public Task<byte[]> CreateAsync(ParticipantClientDefinition definition, CancellationToken cancellationToken = default)
        { Definition = definition; return Task.FromResult(new byte[] { 1 }); }
    }
    private sealed class StubTemplates(Func<Stream, CancellationToken, Task<TemplateValidationResult>> read) : ITemplateManifestService
    {
        public TemplateValidationResult Parse(ReadOnlyMemory<byte> bytes) => throw new NotSupportedException();
        public Task<TemplateValidationResult> ReadAsync(Stream stream, CancellationToken cancellationToken = default) => read(stream, cancellationToken);
    }
    private sealed class StubDefinitions(Func<Stream, CancellationToken, Task<ParticipantDefinitionValidationResult>> read) : IParticipantClientDefinitionService
    {
        public ParticipantDefinitionValidationResult Parse(ReadOnlyMemory<byte> bytes, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<ParticipantDefinitionValidationResult> ReadAsync(Stream stream, CancellationToken cancellationToken = default) => read(stream, cancellationToken);
    }
}
