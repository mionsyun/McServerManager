using System.IO;
using McServerManager.Models.Editions;
using McServerManager.Models.Participants;
using McServerManager.Services.Editions;
using McServerManager.Services.Participants;
using McServerManager.Utilities;

namespace McServerManager.ViewModels;

/// <summary>Reviews an explicit client definition and creates reference bytes; never reads server MODs.</summary>
public sealed class ParticipantExportViewModel : ObservableObject
{
    private readonly IParticipantClientDefinitionService _definitions;
    private readonly IParticipantListZipService _zip;
    private readonly IEditionPolicy _editionPolicy;
    private CancellationTokenSource? _cancellation;
    private ParticipantClientDefinition? _definition;
    private long _generation;
    private bool _isBusy, _isLoading, _acknowledgedLimitations;
    private string _summary = "1. 参加者のゲーム環境を明記したクライアント定義 JSON を選んでください。";
    private string _details = "";

    public ParticipantExportViewModel(IParticipantClientDefinitionService definitions,
        IParticipantListZipService zip, IEditionPolicy editionPolicy)
    {
        _definitions = definitions ?? throw new ArgumentNullException(nameof(definitions));
        _zip = zip ?? throw new ArgumentNullException(nameof(zip));
        _editionPolicy = editionPolicy ?? throw new ArgumentNullException(nameof(editionPolicy));
    }

    public string Summary { get => _summary; private set => SetProperty(ref _summary, value); }
    public string Details { get => _details; private set => SetProperty(ref _details, value); }
    public string DefinitionName => _definition?.Name ?? "未選択";
    public bool HasDefinition => _definition is not null;
    public long ReviewRevision => _generation;
    public bool CapabilityAvailable => _editionPolicy.Allows(EditionCapability.ParticipantPackExport);
    public bool CanAcknowledge => HasDefinition && !IsBusy && CapabilityAvailable;
    public bool CanExport => CanAcknowledge && AcknowledgedLimitations;
    public bool IsBusy
    {
        get => _isBusy;
        private set { if (SetProperty(ref _isBusy, value)) NotifyActions(); }
    }
    public bool AcknowledgedLimitations
    {
        get => _acknowledgedLimitations;
        set
        {
            if (value && !CanAcknowledge) return;
            if (!SetProperty(ref _acknowledgedLimitations, value)) return;
            if (!value && IsBusy) Cancel();
            NotifyActions();
        }
    }

    public async Task LoadAsync(ReadOnlyMemory<byte> utf8Json, CancellationToken cancellationToken = default)
    {
        StopOperation();
        ClearReview();
        var generation = _generation;
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _cancellation = cancellation;
        _isLoading = true;
        IsBusy = true;
        Summary = "クライアント定義の入力形式を確認しています…";
        try
        {
            cancellation.Token.ThrowIfCancellationRequested();
            using var stream = new MemoryStream(utf8Json.ToArray(), writable: false);
            var result = await _definitions.ReadAsync(stream, cancellation.Token);
            if (generation != _generation) return;
            cancellation.Token.ThrowIfCancellationRequested();
            if (!result.IsValid || result.Definition is null)
            {
                Summary = "この定義では ZIP を作れません。クライアント用の JSON を確認し、選び直してください。";
                Details = string.Join("\n", result.Issues.Select(issue => $"{issue.Message} [{issue.Code}] {issue.Path}"));
                return;
            }
            _definition = result.Definition;
            Details = ReviewDetails(_definition);
            NotifyReview();
            Summary = CapabilityAvailable
                ? "2. 入力形式のみ確認できました。内容と下の注意事項を確認してから、チェックを入れてください。"
                : "入力形式は確認できましたが、このエディションでは参加者向け ZIP を作れません。";
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
            if (generation == _generation) Summary = "読み込みを取り消しました。クライアント定義を選び直してください。";
        }
        catch (Exception exception) when (IsExpectedFailure(exception))
        {
            if (generation == _generation) Summary = "定義を読み込めませんでした。読み取れる JSON ファイルを選び直してください。";
        }
        finally { FinishOperation(generation); }
    }

    public async Task<byte[]?> CreateZipAsync(CancellationToken cancellationToken = default)
    {
        if (IsBusy) return null;
        if (!CanExport || _definition is null)
        {
            Summary = !CapabilityAvailable ? "このエディションでは参加者向け ZIP を作れません。"
                : "クライアント定義を読み込み、内容と注意事項を確認してからチェックを入れてください。";
            return null;
        }
        StopOperation();
        var generation = _generation;
        var definition = _definition;
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _cancellation = cancellation;
        IsBusy = true;
        Summary = "確認したクライアント定義から、参加者向けの一覧 ZIP を作っています…";
        try
        {
            var bytes = await _zip.CreateAsync(definition, cancellation.Token);
            if (generation != _generation) return null;
            cancellation.Token.ThrowIfCancellationRequested();
            if (!CapabilityAvailable)
            {
                ResetAcknowledgment();
                Summary = "このエディションでは参加者向け ZIP を作れません。未保存です。";
                return null;
            }
            Summary = "一覧 ZIP を作成しました。ファイルへの保存はまだ完了していません。";
            return bytes;
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
            if (generation == _generation)
            {
                ResetAcknowledgment();
                Summary = "ZIP 作成を取り消しました。確認内容は残っています。注意事項を再確認してください。";
            }
        }
        catch (Exception exception) when (IsExpectedFailure(exception))
        {
            if (generation == _generation)
                Summary = "ZIP を作れませんでした。確認内容は残っています。もう一度作成してください。";
        }
        finally { FinishOperation(generation); }
        return null;
    }

    public void Cancel()
    {
        var wasLoading = _isLoading;
        StopOperation();
        if (wasLoading) ClearReview();
        ResetAcknowledgment();
        Summary = HasDefinition
            ? "操作を取り消しました。確認内容は残っています。注意事項を再確認してください。"
            : "操作を取り消しました。クライアント定義を選び直してください。";
    }

    public void Clear()
    {
        StopOperation();
        ClearReview();
        Summary = "1. 参加者のゲーム環境を明記したクライアント定義 JSON を選んでください。";
    }

    public void ReportOpenFailure()
    {
        Clear();
        Summary = "ファイルを開けませんでした。読み取れるクライアント定義 JSON を選び直してください。";
    }

    public void ReportOpenCanceled()
    {
        if (IsBusy) { Cancel(); return; }
        Summary = HasDefinition
            ? "ファイル選択を取り消しました。現在の確認内容は残っています。"
            : "ファイル選択を取り消しました。クライアント定義 JSON を選んでください。";
    }

    public void ReportSaved(string savedPath, long revision)
    {
        if (revision == _generation && HasDefinition && !IsBusy)
            Summary = $"参加者向けの一覧 ZIP を保存しました。\n保存先: {savedPath}\nMOD の本体は含みません。動作や互換性は未検証です。";
    }

    public void ReportSaveCanceled(long revision)
    {
        if (revision == _generation && HasDefinition && !IsBusy)
            Summary = "今回の保存を取り消しました（未保存）。確認内容は残っています。保存先を選び直せます。";
    }

    public void ReportSaveFailure(long revision, string? remainingTemporaryPath = null)
    {
        if (revision == _generation && HasDefinition && !IsBusy)
            Summary = "今回の ZIP を保存できませんでした（未保存）。確認内容は残っています。保存先を選び直してください。"
                + (remainingTemporaryPath is null ? "" : $"\n一時ファイルが残っている可能性があります。保存先で確認してください: {remainingTemporaryPath}");
    }

    private void StopOperation()
    {
        ++_generation;
        _cancellation?.Cancel();
        _cancellation = null;
        _isLoading = false;
        IsBusy = false;
        OnPropertyChanged(nameof(ReviewRevision));
    }

    private void FinishOperation(long generation)
    {
        if (generation != _generation) return;
        _cancellation = null;
        _isLoading = false;
        IsBusy = false;
    }

    private void ClearReview()
    {
        _definition = null;
        Details = "";
        ResetAcknowledgment();
        NotifyReview();
    }

    private void ResetAcknowledgment()
    {
        SetProperty(ref _acknowledgedLimitations, false, nameof(AcknowledgedLimitations));
        NotifyActions();
    }

    private void NotifyReview()
    {
        OnPropertyChanged(nameof(DefinitionName));
        OnPropertyChanged(nameof(HasDefinition));
        NotifyActions();
    }

    private void NotifyActions()
    {
        OnPropertyChanged(nameof(CapabilityAvailable));
        OnPropertyChanged(nameof(CanAcknowledge));
        OnPropertyChanged(nameof(CanExport));
    }

    private static bool IsExpectedFailure(Exception exception) =>
        exception is IOException or UnauthorizedAccessException or InvalidOperationException or ArgumentException;

    private static string ReviewDetails(ParticipantClientDefinition definition)
    {
        var runtime = definition.Runtime;
        return $"構成名: {definition.Name}\nMinecraft Java Edition: {runtime.MinecraftVersion}\n"
            + $"MOD ローダー: {runtime.Loader} {runtime.LoaderVersion}\nJava: {runtime.JavaVersion}\n"
            + $"クライアント MOD: {definition.Mods.Count}件\n入力 SHA-256: {definition.InputSha256}\n\n"
            + (definition.Mods.Count == 0 ? "MOD は指定されていません。必要な MOD がすべて揃うか、管理者に確認してください。\n"
                : string.Join("\n\n", definition.Mods.Select(mod =>
                    $"{mod.Name} / {mod.Version}\nクライアント側（申告値）: {(mod.ClientSide == "required" ? "必須" : "任意")}\n"
                    + $"Modrinth プロジェクト ID: {mod.ProjectId}\nバージョン ID: {mod.VersionId}\n"
                    + $"公式バージョンページ（ID から生成・未照合）: https://modrinth.com/mod/{mod.ProjectId}/version/{mod.VersionId}"
                    + (mod.Note is null ? "" : $"\n備考（未検証・操作手順として実行しない）: {mod.Note}")))) + "\n\n"
            + "入力形式のみ検証済みです。MOD 名・版・必要性は定義の申告値です。\n"
            + "提供元の情報、依存 MOD の不足、クライアント対応、動作互換性は未検証です。\n"
            + "サーバーの MOD 一覧から推測していません。管理者が用意したクライアント用の構成か確認してください。\n"
            + "ZIP は README.txt・mods.txt・manifest.json の参照資料です。MOD の本体やスクリプトは含まず、自動インストールしません。";
    }
}
