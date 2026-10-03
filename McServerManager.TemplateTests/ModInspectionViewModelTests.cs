using System.Net.Http;
using McServerManager.Models.Modrinth;
using McServerManager.Services.Modrinth;
using McServerManager.ViewModels;

namespace McServerManager.TemplateTests;

public sealed class ModInspectionViewModelTests
{
    [Fact]
    public async Task CapturesExactInputsAndNeverClaimsInstallation()
    {
        ModrinthInspectionRequest? captured = null;
        var vm = new ModInspectionViewModel(new Fake((request, _) =>
        { captured = request; return Task.FromResult(new ModrinthInspectionResult { IsResolved = true }); }));
        vm.VersionIds = "RVFHfo1D";
        vm.DependencyVersionIds = "Mys3P7lK, eRRZzGMc";
        vm.SetInstalledFiles(["installed.jar"]);
        await vm.InspectAsync();
        Assert.Equal("RVFHfo1D", Assert.Single(captured!.SelectedPins).VersionId);
        Assert.Equal(2, captured.DependencyPins.Count);
        Assert.Equal("installed.jar", Assert.Single(captured.InstalledLocalFiles));
        Assert.Contains("宣言上", vm.Summary);
        Assert.Contains("起動や安全性を保証", vm.Summary);
        Assert.Contains("行っていません", vm.Summary);
    }

    [Fact]
    public async Task InputChangeCancelsAndSuppressesObsoleteSuccess()
    {
        var completion = new TaskCompletionSource<ModrinthInspectionResult>();
        CancellationToken token = default;
        var vm = new ModInspectionViewModel(new Fake((_, ct) => { token = ct; return completion.Task; }));
        var task = vm.InspectAsync();
        vm.MinecraftVersion = "1.20.1";
        Assert.True(token.IsCancellationRequested);
        completion.SetResult(new() { IsResolved = true });
        await task;
        Assert.DoesNotContain("満たされています", vm.Summary);
        Assert.False(vm.IsBusy);
    }

    [Fact]
    public async Task NetworkFailureDoesNotExposePrivatePathsOrKeepSuccess()
    {
        var vm = new ModInspectionViewModel(new Fake((_, _) => throw new HttpRequestException("secretpath")));
        await vm.InspectAsync();
        Assert.Contains("完了できません", vm.Summary);
        Assert.DoesNotContain("secretpath", vm.Summary);
        Assert.False(vm.IsBusy);
    }

    [Fact]
    public async Task BlockingFindingsRemainVisibleWithDependencyChain()
    {
        var vm = new ModInspectionViewModel(new Fake((_, _) => Task.FromResult(new ModrinthInspectionResult
        {
            IsResolved = false, Findings = [new() { Code = "MissingDependency", Severity = ModrinthFindingSeverity.Blocker,
                Message = "Required MOD is absent", Dependency = "fabric", Chain = ["RVFHfo1D", "P7dR8mSH"] }]
        })));
        await vm.InspectAsync();
        Assert.Contains("適用できません", vm.Summary);
        Assert.Contains("RVFHfo1D → P7dR8mSH", vm.Details);
        Assert.Contains("MissingDependency", vm.Details);
        vm.JavaVersion = "17";
        Assert.Empty(vm.Details);
        Assert.Contains("もう一度", vm.Summary);
    }

    private sealed class Fake(Func<ModrinthInspectionRequest, CancellationToken, Task<ModrinthInspectionResult>> inspect) : IModrinthInspectionService
    {
        public Task<ModrinthInspectionResult> InspectAsync(ModrinthInspectionRequest request, CancellationToken cancellationToken = default) => inspect(request, cancellationToken);
    }
}
