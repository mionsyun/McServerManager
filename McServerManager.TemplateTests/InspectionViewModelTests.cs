using McServerManager.Models.Templates;
using McServerManager.Services.Templates;
using McServerManager.ViewModels;
using Xunit;

namespace McServerManager.TemplateTests;

public sealed class InspectionViewModelTests
{
    [Fact]
    public async Task InvalidInputShowsActionableFailureAndNeverBecomesSuccess()
    {
        var vm = new TemplateInspectionViewModel(new StubService((_, _) => Task.FromResult(Invalid())));
        await vm.InspectAsync(new MemoryStream());
        Assert.Contains("別のテンプレート", vm.Summary);
        Assert.Contains("invalid", vm.Summary);
        Assert.False(vm.IsBusy);
    }

    [Fact]
    public async Task CancelledOldReadCannotOverwriteNewerResult()
    {
        var oldRead = new TaskCompletionSource<TemplateValidationResult>();
        var calls = 0;
        var vm = new TemplateInspectionViewModel(new StubService((_, _) =>
            ++calls == 1 ? oldRead.Task : Task.FromResult(Invalid())));
        var oldTask = vm.InspectAsync(new MemoryStream());
        Assert.True(vm.IsBusy);
        vm.Cancel();
        await vm.InspectAsync(new MemoryStream());
        var newerSummary = vm.Summary;
        oldRead.SetResult(Valid());
        await oldTask;
        Assert.Equal(newerSummary, vm.Summary);
        Assert.False(vm.IsBusy);
    }

    [Fact]
    public async Task CancelSignalsTokenAndClearsBusyState()
    {
        CancellationToken token = default;
        var read = new TaskCompletionSource<TemplateValidationResult>();
        var vm = new TemplateInspectionViewModel(new StubService((_, ct) => { token = ct; return read.Task; }));
        var task = vm.InspectAsync(new MemoryStream());
        vm.Cancel();
        Assert.True(token.IsCancellationRequested);
        Assert.False(vm.IsBusy);
        read.SetCanceled(token);
        await task;
        Assert.Contains("取り消しました", vm.Summary);
    }

    [Fact]
    public async Task ValidStructureIsExplicitlyNotCompatibilityOrExecutionVerification()
    {
        var vm = new TemplateInspectionViewModel(new StubService((_, _) => Task.FromResult(Valid())));
        await vm.InspectAsync(new MemoryStream());
        Assert.Contains("形式の確認", vm.Summary);
        Assert.Contains("取得元・互換性・動作はまだ確認していません", vm.Summary);
        Assert.False(vm.IsBusy);
    }

    [Fact]
    public async Task ReadFailureIsRecoverableAndDoesNotLeakPaths()
    {
        var vm = new TemplateInspectionViewModel(new StubService((_, _) => throw new IOException("private-path")));
        await vm.InspectAsync(new MemoryStream());
        Assert.Contains("選び直して", vm.Summary);
        Assert.DoesNotContain("private-path", vm.Summary);
        Assert.False(vm.IsBusy);
    }

    private static TemplateValidationResult Invalid() => new()
    {
        IsValid = false, Issues = [new("invalid", "$", "対応していない形式です")]
    };

    private static TemplateValidationResult Valid() => new()
    {
        IsValid = true, Issues = [], Manifest = new()
        {
            SchemaVersion = "1.0", TemplateId = Guid.NewGuid(), Revision = 1,
            Name = "Fixture", Description = "", Edition = "java",
            Runtime = new() { Type = "vanilla", MinecraftVersion = "1.21.1" },
            Settings = new(), Addons = []
        }
    };

    private sealed class StubService(Func<Stream, CancellationToken, Task<TemplateValidationResult>> read) : ITemplateManifestService
    {
        public TemplateValidationResult Parse(ReadOnlyMemory<byte> utf8Json) => throw new NotSupportedException();
        public Task<TemplateValidationResult> ReadAsync(Stream stream, CancellationToken cancellationToken = default) => read(stream, cancellationToken);
    }
}
