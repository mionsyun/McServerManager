using System.IO;
using System.Net.Http;
using McServerManager.Models.Modrinth;
using McServerManager.Services.Modrinth;
using McServerManager.Utilities;

namespace McServerManager.ViewModels;

/// <summary>Explicit read-only live inspection. No installation or runtime service is reachable.</summary>
public sealed class ModInspectionViewModel(IModrinthInspectionService inspection) : ObservableObject
{
    private CancellationTokenSource? _cancellation;
    private long _generation;
    private bool _isBusy;
    private string _minecraftVersion = "1.21.1", _loaderVersion = "0.19.5", _javaVersion = "21";
    private string _versionIds = "", _dependencyVersionIds = "";
    private string _summary = "確認するMODファイル、またはModrinthの固定版IDを選んでください。";
    private string _details = "";
    private IReadOnlyList<string> _selectedFiles = Array.Empty<string>();
    private IReadOnlyList<string> _installedFiles = Array.Empty<string>();

    public string MinecraftVersion { get => _minecraftVersion; set { if (SetProperty(ref _minecraftVersion, value)) Cancel(); } }
    public string LoaderVersion { get => _loaderVersion; set { if (SetProperty(ref _loaderVersion, value)) Cancel(); } }
    public string JavaVersion { get => _javaVersion; set { if (SetProperty(ref _javaVersion, value)) Cancel(); } }
    public string VersionIds { get => _versionIds; set { if (SetProperty(ref _versionIds, value)) Cancel(); } }
    public string DependencyVersionIds { get => _dependencyVersionIds; set { if (SetProperty(ref _dependencyVersionIds, value)) Cancel(); } }
    public bool IsBusy { get => _isBusy; private set => SetProperty(ref _isBusy, value); }
    public string Summary { get => _summary; private set => SetProperty(ref _summary, value); }
    public string Details { get => _details; private set => SetProperty(ref _details, value); }
    public string FileSelection => $"確認するファイル: {_selectedFiles.Count}件 / 導入済みとして比較: {_installedFiles.Count}件";

    public void SetSelectedFiles(IEnumerable<string> files)
    {
        Cancel();
        _selectedFiles = files.ToArray();
        OnPropertyChanged(nameof(FileSelection));
    }
    public void SetInstalledFiles(IEnumerable<string> files)
    {
        Cancel();
        _installedFiles = files.ToArray();
        OnPropertyChanged(nameof(FileSelection));
    }
    public void ClearFiles()
    {
        SetSelectedFiles(Array.Empty<string>());
        SetInstalledFiles(Array.Empty<string>());
    }

    public async Task InspectAsync()
    {
        Cancel();
        var generation = ++_generation;
        using var cancellation = new CancellationTokenSource();
        _cancellation = cancellation;
        IsBusy = true;
        Details = "";
        Summary = "Modrinthの配布情報と実際のMODファイルを確認しています。MODは実行しません。";
        var request = new ModrinthInspectionRequest
        {
            MinecraftVersion = MinecraftVersion.Trim(), LoaderVersion = LoaderVersion.Trim(), JavaVersion = JavaVersion.Trim(),
            SelectedPins = Pins(VersionIds), DependencyPins = Pins(DependencyVersionIds),
            SelectedLocalFiles = _selectedFiles.ToArray(), InstalledLocalFiles = _installedFiles.ToArray()
        };
        try
        {
            var result = await inspection.InspectAsync(request, cancellation.Token);
            if (generation != _generation || cancellation.IsCancellationRequested) return;
            Summary = result.IsResolved
                ? "確認できた宣言上の前提MOD・対応版は満たされています。起動や安全性を保証するものではありません。"
                : "不足・競合・未確認の条件があるため、この構成は適用できません。下の確認結果を見直してください。";
            Summary += "\nMODのインストール・実行、サーバー起動は行っていません。";
            Details = string.Join("\n", result.Artifacts.Select(a =>
                $"{(a.IsInstalled ? "導入済みとして比較" : a.IsTransitive ? "前提MOD" : "選択したMOD")}: {a.Project?.Title ?? "取得元不明"} / {a.File?.FileName ?? Path.GetFileName(a.SourceName)}\n"
                + $"版ID: {a.Version?.Id ?? "不明"} / SHA-256: {a.Sha256}\n"
                + $"読み取ったMOD宣言: {a.Metadata.Mods.Count}件"))
                + "\n\n" + string.Join("\n", result.Findings.Select(f =>
                    $"{Severity(f.Severity)}: {Describe(f.Code)} [{f.Code}]\n{f.Message}"
                    + (f.Dependency is null ? "" : $"\n対象: {f.Dependency}")
                    + (f.Chain.Count == 0 ? "" : $"\n依存経路: {string.Join(" → ", f.Chain)}")));
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { }
        catch (Exception exception) when (exception is IOException or HttpRequestException or UnauthorizedAccessException or ArgumentException)
        {
            if (generation == _generation)
                Summary = "確認を完了できませんでした。通信、ファイルの読み取り権限、固定版IDと対象バージョンを確認してください。適用はしていません。";
        }
        finally
        {
            if (generation == _generation) { _cancellation = null; IsBusy = false; }
        }
    }

    public void Cancel()
    {
        ++_generation;
        _cancellation?.Cancel();
        _cancellation = null;
        if (IsBusy) Summary = "確認を取り消しました。ファイルや対象の版を選び直せます。";
        else if (!string.IsNullOrEmpty(Details)) Summary = "入力が変わりました。もう一度確認してください。";
        Details = "";
        IsBusy = false;
    }

    public void ReportFileSelectionFailure() => Summary = "ファイル一覧を読めませんでした。アクセスできるファイルを選んでください。";
    private static IReadOnlyList<ModrinthPin> Pins(string value) => value.Split([' ', ',', ';', '\r', '\n', '\t'], StringSplitOptions.RemoveEmptyEntries)
        .Select(id => new ModrinthPin { VersionId = id }).ToArray();
    private static string Severity(ModrinthFindingSeverity severity) => severity switch
    { ModrinthFindingSeverity.Blocker => "停止", ModrinthFindingSeverity.Warning => "注意", _ => "情報" };
    private static string Describe(string code) => code switch
    {
        "RuntimeMismatch" or "IncompatiblePlatform" => "対象のMinecraft・Fabric Loader・Javaの版に対応していません",
        "UnresolvedProjectDependency" => "必要な前提MODの固定版IDを指定してください",
        "MissingRequiredDependency" => "JARが要求する前提MODを確認できません",
        "Conflict" => "同時に使えないMODの組み合わせです",
        "SoftConflict" => "作者が併用に注意を求めています",
        "UnsupportedNestedAlternatives" or "DuplicateModId" => "同じMOD IDを持つ複数候補を自動選択できません",
        "AlreadyInstalled" => "選んだ導入済みファイルで前提を満たしています",
        "OptionalSuggestion" => "任意の追加候補です（自動では追加しません）",
        "ApiUnavailable" or "DownloadUnavailable" => "配布元の情報またはファイルを取得できません",
        "UnsupportedConstraint" => "このバージョン条件はまだ判定できません",
        "UnverifiedLocalSource" => "このファイルの正規の取得元を確認できません",
        "MissingFabricMetadata" => "FabricのMOD宣言が見つかりません",
        "ClientOnly" => "クライアント専用のMODはサーバーへ追加できません",
        "RequiredSatisfied" or "TransitiveRequired" => "確認した実ファイルで前提を満たしています",
        "RuntimeSatisfied" => "指定した対象の版は、この宣言を満たしています",
        _ => "詳細を確認してください"
    };
}
