using McServerManager.Models.Editions;
using McServerManager.Models.Templates;
using McServerManager.Models.VanillaRuntime;
using McServerManager.Services.Editions;
using McServerManager.Services.Templates;
using McServerManager.Services.VanillaRuntime;
using McServerManager.ViewModels;

namespace McServerManager.TemplateTests.VanillaRuntime;

public sealed class VanillaRuntimeInspectionViewModelTests
{
    private static readonly string Hash = new('a', 64);
    private static TemplateRuntime Runtime(string version = "1.21.1") => new() { Type = "vanilla", MinecraftVersion = version };
    private static VanillaRuntimeInspectionResult Verified(string version = "1.21.1") => new(
        VanillaRuntimeInspectionStatus.Verified, "Verified", new(version, new('b', 40),
            "https://piston-data.mojang.com/v1/objects/" + new string('c', 40) + "/server.jar", 3, new('c', 40), new('d', 64)));

    [Fact]
    public async Task SelectingValidTemplateDoesNotDownloadUntilExplicitCheck()
    {
        var service = new Stub();
        var vm = new TemplateInspectionViewModel(new TemplateManifestService(), service);
        await using var input = File.OpenRead(Path.Combine(AppContext.BaseDirectory, "Fixtures", "friend-vanilla.maipilot-template.json"));
        await vm.InspectAsync(input);
        Assert.True(vm.RuntimeInspection.CanInspect); Assert.Equal(0, service.Calls);
        Assert.Null(vm.RuntimeInspection.Evidence);
        Assert.False(new EditionPolicy(AppEdition.Free).Allows(EditionCapability.ProductionTemplateApply));
        Assert.False(new EditionPolicy(AppEdition.Pro).Allows(EditionCapability.ProductionTemplateApply));
        await vm.RuntimeInspection.InspectAsync();
        Assert.Equal(1, service.Calls);
        Assert.Equal(vm.OriginalManifestSha256, vm.RuntimeInspection.VerifiedTemplateSha256);
        Assert.Contains("テンプレート全体は未確認", vm.RuntimeInspection.Summary);
        vm.Cancel();
        Assert.Null(vm.RuntimeInspection.Evidence); Assert.Null(vm.RuntimeInspection.VerifiedTemplateSha256);
        Assert.False(vm.RuntimeInspection.CanInspect);
    }

    [Fact]
    public async Task VerifiedEvidenceIsBoundToExactSelectedTemplateAndVersion()
    {
        var service = new Stub();
        var vm = new VanillaRuntimeInspectionViewModel(service);
        vm.SetTemplate(Runtime(), Hash);
        await vm.InspectAsync();
        Assert.Equal(Hash, vm.VerifiedTemplateSha256); Assert.Equal("1.21.1", vm.Evidence!.MinecraftVersion);
        Assert.Equal(100, vm.ProgressPercent); Assert.Contains(Hash, vm.Details);
        vm.SetTemplate(Runtime(), new string('f', 64));
        Assert.Null(vm.Evidence); Assert.Null(vm.VerifiedTemplateSha256); Assert.Empty(vm.Details);
        Assert.Equal(1, service.Calls);
    }

    [Theory]
    [InlineData("paper")] [InlineData("fabric")] [InlineData("forge")] [InlineData("unknown")]
    public async Task UnsupportedFamiliesDoNotReachNetwork(string family)
    {
        var service = new Stub(); var vm = new VanillaRuntimeInspectionViewModel(service);
        vm.SetTemplate(Runtime() with { Type = family }, Hash);
        Assert.False(vm.CanInspect); await vm.InspectAsync(); Assert.Equal(0, service.Calls);
    }

    [Theory]
    [InlineData(null)] [InlineData("")] [InlineData("short")]
    public async Task MissingTemplateIdentityAndMissingServiceCannotVerify(string? hash)
    {
        var service = new Stub(); var vm = new VanillaRuntimeInspectionViewModel(service);
        vm.SetTemplate(Runtime(), hash); await vm.InspectAsync();
        Assert.False(vm.CanInspect); Assert.Equal(0, service.Calls);
        var missing = new VanillaRuntimeInspectionViewModel(null);
        missing.SetTemplate(Runtime(), Hash); await missing.InspectAsync();
        Assert.False(missing.CanInspect); Assert.Null(missing.Evidence);
    }

    [Fact]
    public async Task RepeatClickDoesNotStartSecondDownloadAndProgressHasKnownSize()
    {
        var pending = NewPending();
        var service = new Stub { Run = (_, _, _) => pending.Task };
        var vm = new VanillaRuntimeInspectionViewModel(service); vm.SetTemplate(Runtime(), Hash);
        var task = StartWithInlineProgress(vm);
        Assert.True(vm.IsBusy); Assert.False(vm.CanInspect);
        await vm.InspectAsync(); Assert.Equal(1, service.Calls);
        service.Progress!.Report(new("ServerJar", 2, 4));
        Assert.Equal(50, vm.ProgressPercent); Assert.Contains("2 / 4", vm.ProgressText);
        pending.SetResult(Verified()); await task;
        Assert.False(vm.IsBusy); Assert.True(vm.CanInspect);
    }

    [Fact]
    public async Task LateOldProgressAndSuccessCannotOverwriteNewSelection()
    {
        var pending = NewPending();
        var service = new Stub { Run = (_, _, _) => pending.Task };
        var vm = new VanillaRuntimeInspectionViewModel(service); vm.SetTemplate(Runtime(), Hash);
        var task = StartWithInlineProgress(vm); var oldProgress = service.Progress!; var oldToken = service.Token;
        vm.SetTemplate(Runtime("1.20.1"), new string('f', 64));
        Assert.True(oldToken.IsCancellationRequested);
        var current = vm.Summary;
        oldProgress.Report(new("ServerJar", 3, 3));
        pending.SetResult(Verified()); await task;
        Assert.Equal(current, vm.Summary); Assert.Equal(0, vm.ProgressPercent);
        Assert.Null(vm.Evidence); Assert.Empty(vm.Details);
    }

    [Fact]
    public async Task CancelAndRetrySameSelectionNeverRevivesPriorResult()
    {
        var pending = NewPending();
        var service = new Stub { Run = (_, _, _) => pending.Task };
        var vm = new VanillaRuntimeInspectionViewModel(service); vm.SetTemplate(Runtime(), Hash);
        var old = vm.InspectAsync(); var oldToken = service.Token;
        vm.Cancel(); Assert.True(oldToken.IsCancellationRequested); Assert.Null(vm.Evidence);
        service.Run = (_, _, _) => Task.FromResult(Verified());
        await vm.InspectAsync(); var current = vm.Details;
        pending.SetResult(new(VanillaRuntimeInspectionStatus.Rejected, "HashMismatch")); await old;
        Assert.Equal(current, vm.Details); Assert.Equal(Hash, vm.VerifiedTemplateSha256);
    }

    [Fact]
    public async Task InvalidTemplateReplacementCancelsCurrentRuntimeCheck()
    {
        var pending = NewPending();
        var service = new Stub { Run = (_, _, _) => pending.Task };
        var vm = new TemplateInspectionViewModel(new TemplateManifestService(), service);
        var bytes = await File.ReadAllBytesAsync(Path.Combine(AppContext.BaseDirectory, "Fixtures", "friend-vanilla.maipilot-template.json"));
        await vm.InspectAsync(new MemoryStream(bytes));
        var task = vm.RuntimeInspection.InspectAsync(); var token = service.Token;
        await vm.InspectAsync(new MemoryStream("{}"u8.ToArray()));
        Assert.True(token.IsCancellationRequested);
        pending.SetResult(Verified()); await task;
        Assert.False(vm.RuntimeInspection.CanInspect); Assert.Null(vm.RuntimeInspection.Evidence);
    }

    [Fact]
    public async Task PreCancelledCallAndLateCompletionAfterClearNeverShowSuccess()
    {
        var service = new Stub(); var vm = new VanillaRuntimeInspectionViewModel(service); vm.SetTemplate(Runtime(), Hash);
        using var cancellation = new CancellationTokenSource(); cancellation.Cancel();
        await vm.InspectAsync(cancellation.Token);
        Assert.Equal(0, service.Calls); Assert.False(vm.IsBusy); Assert.Null(vm.Evidence);
        var pending = NewPending(); service.Run = (_, _, _) => pending.Task;
        var task = vm.InspectAsync(); vm.Clear(); pending.SetResult(Verified()); await task;
        Assert.Null(vm.Evidence); Assert.False(vm.CanInspect); Assert.False(vm.IsBusy);
    }

    [Theory]
    [InlineData(VanillaRuntimeInspectionStatus.Rejected)]
    [InlineData(VanillaRuntimeInspectionStatus.Unavailable)]
    [InlineData(VanillaRuntimeInspectionStatus.Unsupported)]
    public async Task ServiceFailureDoesNotRetainEarlierEvidence(VanillaRuntimeInspectionStatus status)
    {
        var service = new Stub(); var vm = new VanillaRuntimeInspectionViewModel(service); vm.SetTemplate(Runtime(), Hash);
        await vm.InspectAsync(); Assert.NotNull(vm.Evidence);
        service.Run = (_, _, _) => Task.FromResult(new VanillaRuntimeInspectionResult(status, "SyntheticFailure"));
        await vm.InspectAsync(); Assert.Null(vm.Evidence); Assert.Null(vm.VerifiedTemplateSha256);
        Assert.Empty(vm.Details); Assert.True(vm.CanInspect); Assert.False(vm.IsBusy);
    }

    [Theory]
    [InlineData("version")] [InlineData("hash")] [InlineData("size")] [InlineData("none")]
    public async Task InconsistentSuccessResultDoesNotBecomeVerification(string change)
    {
        var good = Verified();
        var evidence = change switch
        {
            "version" => good.Evidence! with { MinecraftVersion = "1.20.1" },
            "hash" => good.Evidence! with { Sha256 = "bad" },
            "size" => good.Evidence! with { SizeBytes = 0 },
            _ => null
        };
        var service = new Stub { Run = (_, _, _) => Task.FromResult(good with { Evidence = evidence }) };
        var vm = new VanillaRuntimeInspectionViewModel(service); vm.SetTemplate(Runtime(), Hash);
        await vm.InspectAsync(); Assert.Null(vm.Evidence); Assert.Null(vm.VerifiedTemplateSha256);
    }

    [Fact]
    public async Task ReadFailureDoesNotExposePrivateExceptionDetails()
    {
        var service = new Stub { Run = (_, _, _) => throw new IOException("private-path") };
        var vm = new VanillaRuntimeInspectionViewModel(service); vm.SetTemplate(Runtime(), Hash);
        await vm.InspectAsync(); Assert.DoesNotContain("private-path", vm.Summary);
        Assert.Null(vm.Evidence); Assert.False(vm.IsBusy);
    }

    [Fact]
    public async Task LateErrorAfterClearIsIgnoredAndUnexpectedTimeoutIsNotSuccess()
    {
        var pending = NewPending();
        var service = new Stub { Run = (_, _, _) => pending.Task };
        var vm = new VanillaRuntimeInspectionViewModel(service); vm.SetTemplate(Runtime(), Hash);
        var task = vm.InspectAsync(); vm.Clear(); var summary = vm.Summary;
        pending.SetException(new ObjectDisposedException("old transport")); await task;
        Assert.Equal(summary, vm.Summary); Assert.Null(vm.Evidence);
        vm.SetTemplate(Runtime(), Hash);
        service.Run = (_, _, _) => throw new OperationCanceledException("transport-timeout");
        await vm.InspectAsync(); Assert.Null(vm.Evidence); Assert.False(vm.IsBusy);
    }

    private static TaskCompletionSource<VanillaRuntimeInspectionResult> NewPending() => new(TaskCreationOptions.RunContinuationsAsynchronously);
    private static Task StartWithInlineProgress(VanillaRuntimeInspectionViewModel vm)
    {
        var old = SynchronizationContext.Current;
        try { SynchronizationContext.SetSynchronizationContext(new InlineContext()); return vm.InspectAsync(); }
        finally { SynchronizationContext.SetSynchronizationContext(old); }
    }
    private sealed class InlineContext : SynchronizationContext
    { public override void Post(SendOrPostCallback action, object? state) => action(state); }
    private sealed class Stub : IVanillaRuntimeInspectionService
    {
        public int Calls { get; private set; }
        public CancellationToken Token { get; private set; }
        public IProgress<VanillaRuntimeInspectionProgress>? Progress { get; private set; }
        public Func<string, IProgress<VanillaRuntimeInspectionProgress>?, CancellationToken, Task<VanillaRuntimeInspectionResult>> Run { get; set; }
            = (version, _, _) => Task.FromResult(Verified(version));
        public Task<VanillaRuntimeInspectionResult> InspectAsync(string minecraftVersion,
            IProgress<VanillaRuntimeInspectionProgress>? progress = null, CancellationToken cancellationToken = default)
        { Calls++; Token = cancellationToken; Progress = progress; return Run(minecraftVersion, progress, cancellationToken); }
    }
}
