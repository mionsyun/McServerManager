using System.IO;
using McServerManager.Models.Participants;
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
    private string _participantDefinitionStatus = "テンプレートを選び、参加者用の構成定義の有無を確認してください。";
    private ParticipantClientDefinition? _clientDefinition;
    private string? _originalManifestSha256;

    public TemplateInspectionViewModel(ITemplateManifestService manifests) => _manifests = manifests;

    public bool IsBusy
    {
        get => _isBusy;
        private set { if (SetProperty(ref _isBusy, value)) OnPropertyChanged(nameof(CanReviewParticipantDefinition)); }
    }
    public string Summary { get => _summary; private set => SetProperty(ref _summary, value); }
    public ParticipantClientDefinition? ClientDefinition => _clientDefinition;
    public string? OriginalManifestSha256 => _originalManifestSha256;
    public bool CanReviewParticipantDefinition => !IsBusy && ClientDefinition is not null && IsManifestHash(OriginalManifestSha256);
    public string ParticipantDefinitionStatus
    {
        get => _participantDefinitionStatus;
        private set => SetProperty(ref _participantDefinitionStatus, value);
    }

    public async Task InspectAsync(Stream stream, CancellationToken cancellationToken = default)
    {
        Cancel();
        var generation = ++_generation;
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _cancellation = cancellation;
        IsBusy = true;
        Summary = "ファイルの形式を確認しています…";
        ParticipantDefinitionStatus = "テンプレートを確認中です。参加者用の案内はまだ作れません。";
        try
        {
            cancellation.Token.ThrowIfCancellationRequested();
            var result = await _manifests.ReadAsync(stream, cancellation.Token);
            if (generation != _generation) return;
            cancellation.Token.ThrowIfCancellationRequested();
            if (!result.IsValid || result.Manifest is null)
            {
                Summary = "このファイルは確認できません。別のテンプレートを選んでください。\n"
                    + string.Join("\n", result.Issues.Select(i => $"{DescribeIssue(i.Code)} ({i.Code})"));
                ParticipantDefinitionStatus = "テンプレートの形式が不正なため、参加者用の案内は作れません。clientDefinition も確認してください。";
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
            SetParticipantDefinition(manifest.ClientDefinition, result.ManifestSha256);
            ParticipantDefinitionStatus = manifest.ClientDefinition is null
                ? "参加者用の構成定義がありません。サーバーの MOD 一覧からは推測せず、このテンプレートから案内は作れません。管理者にクライアント定義付きテンプレートを依頼してください。"
                : !IsManifestHash(result.ManifestSha256)
                    ? "元テンプレートの SHA-256 を確認できないため、参加者用の案内は作れません。"
                    : "参加者用の構成定義の入力形式のみ確認済みです。取得元・依存 MOD・クライアント対応・動作互換性は未検証です。内容を確認できます。";
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
            if (generation == _generation)
            {
                Summary = "確認を取り消しました。別のファイルを選べます。";
                ParticipantDefinitionStatus = "確認を取り消したため、参加者用の案内は作れません。テンプレートを選び直してください。";
            }
        }
        catch (IOException)
        {
            if (generation == _generation) ReportReadFailure("ファイルを読めませんでした。使用中でないか確認し、選び直してください。");
        }
        catch (UnauthorizedAccessException)
        {
            if (generation == _generation) ReportReadFailure("ファイルを読む権限がありません。読み取れるファイルを選んでください。");
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
        if (IsBusy || OriginalManifestSha256 is not null) Summary = "確認を取り消しました。別のファイルを選べます。";
        SetParticipantDefinition(null, null);
        ParticipantDefinitionStatus = "確認を取り消したため、参加者用の案内は作れません。テンプレートを選び直してください。";
        IsBusy = false;
    }

    private void SetParticipantDefinition(ParticipantClientDefinition? definition, string? manifestSha256)
    {
        _originalManifestSha256 = IsManifestHash(manifestSha256) ? manifestSha256 : null;
        _clientDefinition = _originalManifestSha256 is null ? null : definition;
        OnPropertyChanged(nameof(ClientDefinition));
        OnPropertyChanged(nameof(OriginalManifestSha256));
        OnPropertyChanged(nameof(CanReviewParticipantDefinition));
    }

    private void ReportReadFailure(string summary)
    {
        Summary = summary;
        ParticipantDefinitionStatus = "テンプレートを読み取れないため、参加者用の案内は作れません。ファイルを選び直してください。";
    }

    private static bool IsManifestHash(string? hash) => hash is { Length: 64 }
        && hash.All(character => character is >= '0' and <= '9' or >= 'a' and <= 'f');

    private static string DescribeIssue(string code) => code switch
    {
        "ManifestTooLarge" => "ファイルが大きすぎます（上限1 MiB）。",
        "ClientMinecraftVersionMismatch" => "参加者用定義とサーバーの Minecraft バージョンが一致していません。",
        "DefinitionTooLarge" => "参加者用 clientDefinition が大きすぎます（上限256 KiB）。",
        "TooManyMods" => "参加者用 MOD の件数が上限256件を超えています。",
        "DuplicateMod" => "参加者用 MOD のプロジェクトまたはバージョンが重複しています。",
        "UnpinnedVersion" or "InvalidPin" => "参加者用 MOD または実行環境の正確な識別子・固定バージョンを指定してください。",
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

    public void ReportOpenFailure()
    {
        Cancel();
        ReportReadFailure("ファイルを開けませんでした。読み取れるファイルを選んでください。");
    }
}
