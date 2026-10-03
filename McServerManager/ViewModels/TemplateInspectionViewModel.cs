using System.IO;
using McServerManager.Services.Templates;
using McServerManager.Utilities;

namespace McServerManager.ViewModels;

/// <summary>Read-only preview. No provisioning, provider or runtime service is reachable here.</summary>
public sealed class TemplateInspectionViewModel : ObservableObject
{
    private readonly ITemplateManifestService _manifests;
    private CancellationTokenSource? _cancellation;
    private long _generation;
    private bool _isBusy;
    private string _summary = "テンプレートを選ぶと、ファイルの形式と設定を確認できます。";

    public TemplateInspectionViewModel(ITemplateManifestService manifests) => _manifests = manifests;

    public bool IsBusy { get => _isBusy; private set => SetProperty(ref _isBusy, value); }
    public string Summary { get => _summary; private set => SetProperty(ref _summary, value); }

    public async Task InspectAsync(Stream stream)
    {
        Cancel();
        var generation = ++_generation;
        using var cancellation = new CancellationTokenSource();
        _cancellation = cancellation;
        IsBusy = true;
        Summary = "ファイルの形式を確認しています…";
        try
        {
            var result = await _manifests.ReadAsync(stream, cancellation.Token);
            if (generation != _generation || cancellation.IsCancellationRequested) return;
            if (!result.IsValid || result.Manifest is null)
            {
                Summary = "このファイルは確認できません。別のテンプレートを選んでください。\n"
                    + string.Join("\n", result.Issues.Select(i => $"{DescribeIssue(i.Code)} ({i.Code})"));
                return;
            }
            var manifest = result.Manifest;
            var runtime = manifest.Runtime;
            var settings = manifest.Settings;
            Summary = $"形式の確認ができました: {manifest.Name}\n"
                + $"サーバー: {runtime.Type} / Minecraft {runtime.MinecraftVersion}\n"
                + (runtime.Build is null ? "" : $"固定ビルド: {runtime.Build}\n")
                + (runtime.LoaderVersion is null ? "" : $"Loader: {runtime.LoaderVersion} / Installer: {runtime.InstallerVersion}\n")
                + $"追加MOD・プラグイン: {manifest.Addons.Count}件\n"
                + $"最大人数: {settings.MaxPlayers?.ToString() ?? "指定なし"}\n"
                + $"難易度: {settings.Difficulty ?? "指定なし"} / モード: {settings.Gamemode ?? "指定なし"}\n"
                + $"描画距離: {settings.ViewDistance?.ToString() ?? "指定なし"} / シミュレーション距離: {settings.SimulationDistance?.ToString() ?? "指定なし"}\n"
                + $"PvP: {(settings.Pvp.HasValue ? (settings.Pvp.Value ? "有効" : "無効") : "指定なし")} / スポーン保護: {settings.SpawnProtection?.ToString() ?? "指定なし"}\n"
                + "\n取得元・互換性・動作はまだ確認していません。ファイルは変更していません。";
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
            if (generation == _generation) Summary = "確認を取り消しました。別のファイルを選べます。";
        }
        catch (IOException)
        {
            if (generation == _generation) Summary = "ファイルを読めませんでした。使用中でないか確認し、選び直してください。";
        }
        catch (UnauthorizedAccessException)
        {
            if (generation == _generation) Summary = "ファイルを読む権限がありません。読み取れるファイルを選んでください。";
        }
        finally
        {
            if (generation == _generation)
            {
                _cancellation = null;
                IsBusy = false;
            }
        }
    }

    public void Cancel()
    {
        ++_generation;
        _cancellation?.Cancel();
        _cancellation = null;
        if (IsBusy) Summary = "確認を取り消しました。別のファイルを選べます。";
        IsBusy = false;
    }

    private static string DescribeIssue(string code) => code switch
    {
        "ManifestTooLarge" => "ファイルが大きすぎます（上限1 MiB）。",
        "InvalidUtf8" or "InvalidUnicode" => "文字コードが対応していません。UTF-8のファイルを選んでください。",
        "InvalidJson" => "JSONが壊れているか、入れ子が深すぎます。",
        "DuplicateProperty" => "同じ設定項目が重複しています。",
        "UnknownProperty" => "対応していない設定項目が含まれています。",
        "MissingProperty" => "必要な項目が不足しています。",
        "MissingRuntimePin" or "InvalidRuntimePin" => "サーバー本体の固定バージョン指定が不足しているか、不正です。",
        "ProviderDisabled" => "CurseForgeの項目には現在対応していません。",
        "UnsafeFileName" => "追加ファイルの名前に使えない文字やパスが含まれています。",
        "TotalSizeExceeded" => "追加ファイルの合計が上限2 GiBを超えています。",
        "LimitExceeded" => "項目数が上限を超えています。",
        "DuplicateDependency" => "同じ前提MODが重複して指定されています。",
        _ => "対応していない値、組み合わせ、または依存関係が含まれています。"
    };

    public void ReportOpenFailure() => Summary = "ファイルを開けませんでした。読み取れるファイルを選んでください。";
}
