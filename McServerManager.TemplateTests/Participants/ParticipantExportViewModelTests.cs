using System.IO.Compression;
using System.Text;
using System.Text.Json;
using McServerManager.Models.Editions;
using McServerManager.Models.Participants;
using McServerManager.Services.Editions;
using McServerManager.Services.Participants;
using McServerManager.ViewModels;

namespace McServerManager.TemplateTests.Participants;

public sealed class ParticipantExportViewModelTests
{
    [Fact]
    public async Task EmptyStateCannotAcknowledgeOrExport()
    {
        var exporter = new StubZip();
        var vm = NewVm(zip: exporter);
        vm.AcknowledgedLimitations = true;
        Assert.False(vm.HasDefinition);
        Assert.False(vm.CanAcknowledge);
        Assert.False(vm.AcknowledgedLimitations);
        Assert.False(vm.CanExport);
        Assert.Null(await vm.CreateZipAsync());
        Assert.Equal(0, exporter.Calls);
        Assert.Contains("クライアント定義", vm.Summary);
    }

    [Theory]
    [InlineData(AppEdition.Free)]
    [InlineData(AppEdition.Pro)]
    public async Task ValidReviewNeedsExplicitAcknowledgmentAndCreatesReferenceOnlyZip(AppEdition edition)
    {
        var policy = new EditionPolicy(edition);
        var vm = NewVm(policy: policy, zip: new ParticipantListZipService(policy));
        await vm.LoadAsync(Fixture());
        Assert.True(vm.CapabilityAvailable);
        Assert.True(vm.HasDefinition);
        Assert.True(vm.CanAcknowledge);
        Assert.False(vm.CanExport);
        Assert.Null(await vm.CreateZipAsync());
        vm.AcknowledgedLimitations = true;
        Assert.True(vm.CanExport);
        var bytes = Assert.IsType<byte[]>(await vm.CreateZipAsync());
        using var archive = new ZipArchive(new MemoryStream(bytes), ZipArchiveMode.Read);
        Assert.Equal(new[] { "README.txt", "mods.txt", "manifest.json" }, archive.Entries.Select(entry => entry.FullName));
        Assert.False(vm.IsBusy);
        Assert.True(vm.CanExport);
        Assert.Contains("保存はまだ完了していません", vm.Summary);
    }

    [Fact]
    public async Task ReviewShowsAllDeclaredPinsUrlsAndUnverifiedLimitations()
    {
        var vm = NewVm();
        await vm.LoadAsync(Fixture());
        foreach (var detail in new[] { "友達用クライアント", "1.21.1", "fabric 0.16.9", "21.0.2",
            "必須 MOD", "1.2.3+fabric", "任意 MOD", "2.0.0", "必須", "任意", "AbCd1234", "EfGh5678", "IjKl1234", "MnOp5678",
            "https://modrinth.com/mod/AbCd1234/version/EfGh5678", "https://modrinth.com/mod/IjKl1234/version/MnOp5678",
            "入力形式のみ", "申告値", "未検証", "未照合", "依存 MOD", "サーバーの MOD 一覧から推測していません", "自動インストールしません" })
            Assert.Contains(detail, vm.Details);
        Assert.Contains("入力形式のみ", vm.Summary);
        Assert.DoesNotContain("動作確認済", vm.Details);
    }

    [Fact]
    public async Task EmptyModListRequiresAcknowledgmentAndWarnsAboutCompleteness()
    {
        var vm = NewVm();
        await vm.LoadAsync(Fixture(noMods: true));
        Assert.True(vm.HasDefinition);
        Assert.Contains("MOD は指定されていません", vm.Details);
        Assert.Contains("すべて揃うか", vm.Details);
        Assert.False(vm.CanExport);
        vm.AcknowledgedLimitations = true;
        Assert.NotNull(await vm.CreateZipAsync());
    }

    [Theory]
    [InlineData("")]
    [InlineData("{}")]
    [InlineData("not json")]
    [InlineData("{\"definitionKind\":\"server\"}")]
    public async Task InvalidInputRemovesPreviousReviewAndAcknowledgment(string input)
    {
        var exporter = new StubZip();
        var vm = NewVm(zip: exporter);
        await LoadAndAcknowledge(vm);
        await vm.LoadAsync(Encoding.UTF8.GetBytes(input));
        Assert.False(vm.HasDefinition);
        Assert.False(vm.CanExport);
        Assert.False(vm.AcknowledgedLimitations);
        Assert.DoesNotContain("必須 MOD", vm.Details);
        Assert.Contains("ZIP を作れません", vm.Summary);
        Assert.Null(await vm.CreateZipAsync());
        Assert.Equal(0, exporter.Calls);
    }

    [Fact]
    public async Task ReloadEvenSameBytesRequiresFreshAcknowledgment()
    {
        var vm = NewVm();
        await LoadAndAcknowledge(vm);
        var revision = vm.ReviewRevision;
        await vm.LoadAsync(Fixture());
        Assert.True(vm.ReviewRevision > revision);
        Assert.False(vm.AcknowledgedLimitations);
        Assert.False(vm.CanExport);
        Assert.True(vm.HasDefinition);
    }

    [Fact]
    public async Task NewLoadDiscardsOlderResultEvenIfParserIgnoresCancellation()
    {
        var pending = Completion<ParticipantDefinitionValidationResult>();
        CancellationToken oldToken = default;
        var calls = 0;
        var parser = new StubDefinitions((_, token) =>
        {
            if (++calls != 1) return Task.FromResult(Valid("新しい構成"));
            oldToken = token;
            return pending.Task;
        });
        var vm = NewVm(parser);
        var oldLoad = vm.LoadAsync(Fixture());
        Assert.True(vm.IsBusy);
        vm.AcknowledgedLimitations = true;
        Assert.False(vm.AcknowledgedLimitations);
        await vm.LoadAsync(Fixture());
        vm.AcknowledgedLimitations = true;
        var summary = vm.Summary;
        pending.SetResult(Valid("古い構成"));
        await oldLoad;
        Assert.True(oldToken.IsCancellationRequested);
        Assert.Equal("新しい構成", vm.DefinitionName);
        Assert.Equal(summary, vm.Summary);
        Assert.True(vm.CanExport);
        Assert.False(vm.IsBusy);
    }

    [Fact]
    public async Task CancelledLoadCannotLeaveOrRestoreStaleReview()
    {
        var pending = Completion<ParticipantDefinitionValidationResult>();
        var calls = 0;
        var vm = NewVm(new StubDefinitions((_, _) => ++calls == 1 ? Task.FromResult(Valid()) : pending.Task));
        await LoadAndAcknowledge(vm);
        var load = vm.LoadAsync(Fixture());
        Assert.False(vm.HasDefinition);
        vm.Cancel();
        var summary = vm.Summary;
        pending.SetResult(Valid());
        await load;
        Assert.False(vm.IsBusy);
        Assert.False(vm.HasDefinition);
        Assert.False(vm.CanExport);
        Assert.Empty(vm.Details);
        Assert.Equal(summary, vm.Summary);
    }

    [Fact]
    public async Task OlderReadFailureCannotOverwriteNewerReview()
    {
        var pending = Completion<ParticipantDefinitionValidationResult>();
        var calls = 0;
        var vm = NewVm(new StubDefinitions((_, _) => ++calls == 1 ? pending.Task : Task.FromResult(Valid("新しい構成"))));
        var oldLoad = vm.LoadAsync(Fixture());
        await vm.LoadAsync(Fixture());
        var summary = vm.Summary;
        pending.SetException(new IOException("private-path"));
        await oldLoad;
        Assert.Equal("新しい構成", vm.DefinitionName);
        Assert.Equal(summary, vm.Summary);
        Assert.DoesNotContain("private-path", vm.Summary);
    }

    [Fact]
    public async Task ReadSnapshotsCallerOwnedBytesBeforeAwait()
    {
        var gate = Completion<bool>();
        var parser = new StubDefinitions(async (stream, token) =>
        {
            await gate.Task;
            return await new ParticipantClientDefinitionService().ReadAsync(stream, token);
        });
        var input = Fixture();
        var vm = NewVm(parser);
        var load = vm.LoadAsync(input);
        Array.Fill(input, (byte)0);
        gate.SetResult(true);
        await load;
        Assert.Equal("友達用クライアント", vm.DefinitionName);
        Assert.True(vm.HasDefinition);
    }

    [Fact]
    public async Task RepeatedExportDoesNotStartSecondOperation()
    {
        var pending = Completion<byte[]>();
        var zip = new StubZip((_, _) => pending.Task);
        var vm = NewVm(zip: zip);
        await LoadAndAcknowledge(vm);
        var export = vm.CreateZipAsync();
        var summary = vm.Summary;
        Assert.True(vm.IsBusy);
        Assert.False(vm.CanExport);
        Assert.False(vm.CanAcknowledge);
        Assert.Null(await vm.CreateZipAsync());
        Assert.Equal(summary, vm.Summary);
        Assert.Equal(1, zip.Calls);
        pending.SetResult([1, 2, 3]);
        Assert.Equal(new byte[] { 1, 2, 3 }, await export);
        Assert.False(vm.IsBusy);
    }

    [Fact]
    public async Task CancelExportPreservesReviewResetsAcknowledgmentAndDiscardsBytes()
    {
        var pending = Completion<byte[]>();
        var zip = new StubZip((_, _) => pending.Task);
        var vm = NewVm(zip: zip);
        await LoadAndAcknowledge(vm);
        var details = vm.Details;
        var export = vm.CreateZipAsync();
        vm.Cancel();
        Assert.True(zip.LastToken.IsCancellationRequested);
        var summary = vm.Summary;
        pending.SetResult([1]);
        Assert.Null(await export);
        Assert.Equal(details, vm.Details);
        Assert.Equal(summary, vm.Summary);
        Assert.True(vm.HasDefinition);
        Assert.False(vm.AcknowledgedLimitations);
        Assert.False(vm.CanExport);
        Assert.False(vm.IsBusy);
        Assert.Contains("確認内容は残っています", vm.Summary);
    }

    [Fact]
    public async Task RevokingAcknowledgmentDuringExportInvalidatesPendingBytes()
    {
        var pending = Completion<byte[]>();
        var zip = new StubZip((_, _) => pending.Task);
        var vm = NewVm(zip: zip);
        await LoadAndAcknowledge(vm);
        var export = vm.CreateZipAsync();
        vm.AcknowledgedLimitations = false;
        pending.SetResult([1]);
        Assert.Null(await export);
        Assert.True(zip.LastToken.IsCancellationRequested);
        Assert.True(vm.HasDefinition);
        Assert.False(vm.CanExport);
    }

    [Fact]
    public async Task ReloadWhileExportPendingCannotPublishOldBytes()
    {
        var pending = Completion<byte[]>();
        var vm = NewVm(zip: new StubZip((_, _) => pending.Task));
        await LoadAndAcknowledge(vm);
        var export = vm.CreateZipAsync();
        await vm.LoadAsync(Fixture("新しい構成"));
        var summary = vm.Summary;
        pending.SetResult([1]);
        Assert.Null(await export);
        Assert.Equal("新しい構成", vm.DefinitionName);
        Assert.Equal(summary, vm.Summary);
        Assert.False(vm.AcknowledgedLimitations);
        Assert.False(vm.IsBusy);
    }

    [Fact]
    public async Task ExternalCancellationCannotReturnBytesEvenIfExporterIgnoresToken()
    {
        using var cancellation = new CancellationTokenSource();
        var pending = Completion<byte[]>();
        var vm = NewVm(zip: new StubZip((_, _) => pending.Task));
        await LoadAndAcknowledge(vm);
        var export = vm.CreateZipAsync(cancellation.Token);
        cancellation.Cancel();
        pending.SetResult([1]);
        Assert.Null(await export);
        Assert.True(vm.HasDefinition);
        Assert.False(vm.AcknowledgedLimitations);
        Assert.False(vm.IsBusy);
        Assert.Contains("取り消しました", vm.Summary);
    }

    [Fact]
    public async Task PreCanceledLoadClearsOldReviewAndShowsCancellation()
    {
        var vm = NewVm();
        await LoadAndAcknowledge(vm);
        await vm.LoadAsync(Fixture(), new CancellationToken(canceled: true));
        Assert.False(vm.HasDefinition);
        Assert.False(vm.IsBusy);
        Assert.Contains("取り消しました", vm.Summary);
    }

    [Fact]
    public async Task ReadFailureIsSanitizedAndNextLoadCanRecover()
    {
        var calls = 0;
        var vm = NewVm(new StubDefinitions((_, _) => ++calls == 1
            ? throw new UnauthorizedAccessException("private-path") : Task.FromResult(Valid())));
        await vm.LoadAsync(Fixture());
        Assert.False(vm.HasDefinition);
        Assert.False(vm.IsBusy);
        Assert.DoesNotContain("private-path", vm.Summary);
        Assert.Contains("選び直して", vm.Summary);
        await LoadAndAcknowledge(vm);
        Assert.True(vm.CanExport);
    }

    [Fact]
    public async Task ExportFailureLeavesReviewedDataAvailableForRetryWithoutLeakingPaths()
    {
        var calls = 0;
        var vm = NewVm(zip: new StubZip((_, _) => ++calls == 1
            ? throw new IOException("private-path") : Task.FromResult(new byte[] { 1 })));
        await LoadAndAcknowledge(vm);
        Assert.Null(await vm.CreateZipAsync());
        Assert.False(vm.IsBusy);
        Assert.True(vm.CanExport);
        Assert.DoesNotContain("private-path", vm.Summary);
        Assert.Contains("作れませんでした", vm.Summary);
        Assert.NotNull(await vm.CreateZipAsync());
    }

    [Fact]
    public async Task CapabilityDeniedCannotAcknowledgeOrInvokeExporter()
    {
        var policy = new MutablePolicy { Allowed = false };
        var zip = new StubZip();
        var vm = NewVm(zip: zip, policy: policy);
        await vm.LoadAsync(Fixture());
        vm.AcknowledgedLimitations = true;
        Assert.True(vm.HasDefinition);
        Assert.False(vm.CapabilityAvailable);
        Assert.False(vm.CanAcknowledge);
        Assert.False(vm.AcknowledgedLimitations);
        Assert.Null(await vm.CreateZipAsync());
        Assert.Equal(0, zip.Calls);
        Assert.Equal(EditionCapability.ParticipantPackExport, policy.LastCapability);
    }

    [Fact]
    public async Task CapabilityRevokedDuringExportDiscardsBytesAndAcknowledgment()
    {
        var policy = new MutablePolicy();
        var pending = Completion<byte[]>();
        var vm = NewVm(zip: new StubZip((_, _) => pending.Task), policy: policy);
        await LoadAndAcknowledge(vm);
        var export = vm.CreateZipAsync();
        policy.Allowed = false;
        pending.SetResult([1]);
        Assert.Null(await export);
        Assert.False(vm.AcknowledgedLimitations);
        Assert.False(vm.IsBusy);
        Assert.Contains("未保存", vm.Summary);
    }

    [Fact]
    public async Task SaveReportsAreTruthfulAndCannotOverwriteNewerReview()
    {
        var vm = NewVm();
        await LoadAndAcknowledge(vm);
        await vm.CreateZipAsync();
        var revision = vm.ReviewRevision;
        vm.ReportSaveCanceled(revision);
        Assert.Contains("未保存", vm.Summary);
        Assert.True(vm.CanExport);
        vm.ReportSaveFailure(revision);
        Assert.Contains("未保存", vm.Summary);
        Assert.True(vm.CanExport);
        vm.ReportSaved("chosen.zip", revision);
        Assert.Contains("保存しました", vm.Summary);
        Assert.Contains("chosen.zip", vm.Summary);
        await vm.LoadAsync(Fixture("新しい構成"));
        var summary = vm.Summary;
        vm.ReportSaved("old.zip", revision);
        vm.ReportSaveCanceled(revision);
        vm.ReportSaveFailure(revision);
        Assert.Equal(summary, vm.Summary);
    }

    [Fact]
    public async Task SaveCleanupFailureReportsPossibleTemporaryFileWithoutClaimingFinalZipExists()
    {
        var vm = NewVm();
        await LoadAndAcknowledge(vm);
        await vm.CreateZipAsync();
        vm.ReportSaveFailure(vm.ReviewRevision, "selected-folder/.maipilot-participant-stage.tmp");
        Assert.Contains("未保存", vm.Summary);
        Assert.Contains("一時ファイルが残っている可能性", vm.Summary);
        Assert.Contains("selected-folder/.maipilot-participant-stage.tmp", vm.Summary);
        Assert.DoesNotContain("保存しました", vm.Summary);
        Assert.True(vm.CanExport);
    }

    [Fact]
    public async Task CancelPickerPreservesExistingReviewButOpenFailureClearsIt()
    {
        var vm = NewVm();
        await LoadAndAcknowledge(vm);
        var details = vm.Details;
        vm.ReportOpenCanceled();
        Assert.Equal(details, vm.Details);
        Assert.True(vm.CanExport);
        vm.ReportOpenFailure();
        Assert.False(vm.HasDefinition);
        Assert.False(vm.AcknowledgedLimitations);
        Assert.Empty(vm.Details);
        Assert.Contains("選び直して", vm.Summary);
    }

    [Fact]
    public async Task DerivedPropertiesNotifyBindingsAcrossReviewAndAcknowledgment()
    {
        var vm = NewVm();
        var notifications = new List<string?>();
        vm.PropertyChanged += (_, change) => notifications.Add(change.PropertyName);
        await LoadAndAcknowledge(vm);
        foreach (var name in new[] { "HasDefinition", "DefinitionName", "CanAcknowledge", "CanExport",
            "CapabilityAvailable", "IsBusy", "AcknowledgedLimitations", "ReviewRevision", "Details", "Summary" })
            Assert.Contains(name, notifications);
        vm.Clear();
        Assert.False(vm.HasDefinition);
        Assert.False(vm.CanExport);
        Assert.Equal("未選択", vm.DefinitionName);
    }

    private static ParticipantExportViewModel NewVm(IParticipantClientDefinitionService? parser = null,
        IParticipantListZipService? zip = null, IEditionPolicy? policy = null) =>
        new(parser ?? new ParticipantClientDefinitionService(), zip ?? new StubZip(), policy ?? new EditionPolicy(AppEdition.Free));
    private static async Task LoadAndAcknowledge(ParticipantExportViewModel vm)
    {
        await vm.LoadAsync(Fixture());
        vm.AcknowledgedLimitations = true;
    }
    private static TaskCompletionSource<T> Completion<T>() => new(TaskCreationOptions.RunContinuationsAsynchronously);
    private static ParticipantDefinitionValidationResult Valid(string name = "友達用クライアント") =>
        new ParticipantClientDefinitionService().Parse(Fixture(name));
    private static byte[] Fixture(string name = "友達用クライアント", bool noMods = false) => JsonSerializer.SerializeToUtf8Bytes(new
    {
        schemaVersion = "1.0", definitionKind = "client", name,
        runtime = new { minecraftVersion = "1.21.1", loader = "fabric", loaderVersion = "0.16.9", javaVersion = "21.0.2" },
        mods = noMods ? Array.Empty<object>() : new object[]
        {
            new { provider = "modrinth", projectId = "AbCd1234", versionId = "EfGh5678", name = "必須 MOD", version = "1.2.3+fabric", clientSide = "required" },
            new { provider = "modrinth", projectId = "IjKl1234", versionId = "MnOp5678", name = "任意 MOD", version = "2.0.0", clientSide = "optional", note = "管理者に確認してください。" }
        }
    });

    private sealed class StubDefinitions(Func<Stream, CancellationToken, Task<ParticipantDefinitionValidationResult>> read) : IParticipantClientDefinitionService
    {
        public ParticipantDefinitionValidationResult Parse(ReadOnlyMemory<byte> utf8Json, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<ParticipantDefinitionValidationResult> ReadAsync(Stream input, CancellationToken cancellationToken = default) => read(input, cancellationToken);
    }

    private sealed class StubZip(Func<ParticipantClientDefinition, CancellationToken, Task<byte[]>>? create = null) : IParticipantListZipService
    {
        public int Calls { get; private set; }
        public CancellationToken LastToken { get; private set; }
        public Task<byte[]> CreateAsync(ParticipantClientDefinition definition, CancellationToken cancellationToken = default)
        {
            Calls++;
            LastToken = cancellationToken;
            return create?.Invoke(definition, cancellationToken) ?? Task.FromResult(new byte[] { 1, 2, 3 });
        }
    }

    private sealed class MutablePolicy : IEditionPolicy
    {
        public bool Allowed { get; set; } = true;
        public EditionCapability? LastCapability { get; private set; }
        public AppEdition Edition => AppEdition.Free;
        public string DisplayName => "Test";
        public bool Allows(EditionCapability capability) { LastCapability = capability; return Allowed; }
    }
}
