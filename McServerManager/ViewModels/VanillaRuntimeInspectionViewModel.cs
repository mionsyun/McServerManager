using McServerManager.Models.Templates;
using McServerManager.Models.VanillaRuntime;
using McServerManager.Services.VanillaRuntime;
using McServerManager.Utilities;

namespace McServerManager.ViewModels;

/// <summary>User-triggered, read-only runtime-file verification. Never provisions or grants Apply authority.</summary>
public sealed class VanillaRuntimeInspectionViewModel(IVanillaRuntimeInspectionService? service) : ObservableObject
{
    private sealed record Selection(string TemplateSha256, string MinecraftVersion);
    private Selection? _selection;
    private CancellationTokenSource? _cancellation;
    private long _generation;
    private bool _isBusy;
    private string _summary = "Vanilla のテンプレートを選ぶと、公式サーバー本体を通信で確認できます。";
    private string _progressText = "", _details = "";
    private double _progressPercent;
    private VanillaRuntimeArtifactEvidence? _evidence;
    private string? _verifiedTemplateSha256;

    public bool IsBusy { get => _isBusy; private set { if (SetProperty(ref _isBusy, value)) OnPropertyChanged(nameof(CanInspect)); } }
    public bool CanInspect => !IsBusy && service is not null && _selection is not null;
    public string Summary { get => _summary; private set => SetProperty(ref _summary, value); }
    public string ProgressText { get => _progressText; private set => SetProperty(ref _progressText, value); }
    public double ProgressPercent { get => _progressPercent; private set => SetProperty(ref _progressPercent, value); }
    public string Details { get => _details; private set => SetProperty(ref _details, value); }
    public VanillaRuntimeArtifactEvidence? Evidence => _evidence;
    public string? VerifiedTemplateSha256 => _verifiedTemplateSha256;

    public void SetTemplate(TemplateRuntime runtime, string? templateSha256)
    {
        Clear();
        if (runtime is null || !IsHash(templateSha256, 64)) return;
        if (runtime.Type != "vanilla" || runtime.Build is not null || runtime.LoaderVersion is not null || runtime.InstallerVersion is not null)
        { Summary = "本体ファイルの確認は Vanilla のみ対応しています。他の種類の版やビルドは推測しません。"; return; }
        _selection = new(templateSha256!, runtime.MinecraftVersion);
        Summary = service is null ? "本体ファイルの確認サービスを利用できません。"
            : $"Vanilla {runtime.MinecraftVersion} の本体を確認できます。下のボタンを押すまで通信しません。";
        OnPropertyChanged(nameof(CanInspect));
    }

    public async Task InspectAsync(CancellationToken cancellationToken = default)
    {
        if (!CanInspect || _selection is not { } selection || service is null) return;
        var generation = ++_generation;
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _cancellation = cancellation;
        ClearEvidence();
        ProgressPercent = 0;
        ProgressText = "公式配布情報を取得しています…";
        Summary = "Vanilla 本体を取得して確認中です。ファイルへの保存や実行はしません。";
        IsBusy = true;
        var progress = new Progress<VanillaRuntimeInspectionProgress>(value =>
        {
            if (!Current(generation, selection) || !IsBusy || cancellation.IsCancellationRequested) return;
            if (value.Stage == "ServerJar" && value.TotalBytes is > 0 and <= 128L * 1024 * 1024)
            {
                var count = Math.Clamp(value.BytesRead, 0, value.TotalBytes.Value);
                ProgressPercent = 100d * count / value.TotalBytes.Value;
                ProgressText = $"本体を取得・照合中: {count:N0} / {value.TotalBytes.Value:N0} バイト";
            }
            else ProgressText = value.Stage == "VersionManifest" ? "指定した版の公式メタデータを照合しています…" : "公式配布一覧を確認しています…";
        });
        try
        {
            cancellation.Token.ThrowIfCancellationRequested();
            var result = await service.InspectAsync(selection.MinecraftVersion, progress, cancellation.Token);
            if (!Current(generation, selection)) return;
            cancellation.Token.ThrowIfCancellationRequested();
            if (result.Status == VanillaRuntimeInspectionStatus.Verified && result.Evidence is { } evidence
                && evidence.MinecraftVersion == selection.MinecraftVersion
                && evidence.SizeBytes is > 0 and <= 128L * 1024 * 1024
                && IsHash(evidence.Sha1, 40) && IsHash(evidence.Sha256, 64) && IsHash(evidence.VersionManifestSha1, 40))
            {
                _evidence = evidence;
                _verifiedTemplateSha256 = selection.TemplateSha256;
                OnPropertyChanged(nameof(Evidence)); OnPropertyChanged(nameof(VerifiedTemplateSha256));
                ProgressPercent = 100;
                ProgressText = $"本体 {evidence.SizeBytes:N0} バイトの取得・照合が完了しました。保存はしていません。";
                Summary = "Vanilla 本体のサイズと SHA-1 が公式メタデータに一致し、SHA-256 を算出しました。起動・互換性・テンプレート全体は未確認です。";
                Details = $"対象テンプレート SHA-256: {selection.TemplateSha256}\nMinecraft: {evidence.MinecraftVersion}\n"
                    + $"公式版メタデータ SHA-1: {evidence.VersionManifestSha1}\n本体 SHA-1（公式値と照合）: {evidence.Sha1}\n本体 SHA-256（今回算出）: {evidence.Sha256}\n取得元: {evidence.DownloadUrl}";
            }
            else
            {
                Summary = result.Status switch
                {
                    VanillaRuntimeInspectionStatus.Unsupported => "指定した版の本体確認には対応していません。最新の版へ置き換えることはしません。",
                    VanillaRuntimeInspectionStatus.Unavailable => "公式配布情報または本体を取得できませんでした。通信状況を確認して再試行してください。",
                    _ => "本体の取得元・サイズ・ハッシュを確認できないため停止しました。検証済みとして扱いません。"
                };
                ProgressText = "確認は完了していません。";
            }
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        { if (Current(generation, selection)) { Summary = "本体の確認を取り消しました。検証済みの結果はありません。"; ProgressText = ""; } }
        catch (Exception) when (!Current(generation, selection)) { } // Closed/replaced selections cannot revive errors either.
        catch (Exception ex) when (ex is System.Net.Http.HttpRequestException or System.IO.IOException or TimeoutException or OperationCanceledException)
        { if (Current(generation, selection)) { Summary = "通信または読み取りに失敗しました。検証済みの結果はありません。"; ProgressText = ""; } }
        finally
        {
            if (Current(generation, selection)) { _cancellation = null; IsBusy = false; }
        }
    }

    public void Cancel()
    {
        ++_generation;
        _cancellation?.Cancel();
        _cancellation = null;
        ClearEvidence();
        IsBusy = false; ProgressPercent = 0; ProgressText = "";
        Summary = "本体の確認を取り消しました。検証済みの結果はありません。";
    }

    public void Clear()
    {
        Cancel(); _selection = null;
        Summary = "Vanilla のテンプレートを選ぶと、公式サーバー本体を通信で確認できます。";
        OnPropertyChanged(nameof(CanInspect));
    }

    private bool Current(long generation, Selection selection) => generation == _generation && ReferenceEquals(selection, _selection);
    private void ClearEvidence()
    {
        _evidence = null; _verifiedTemplateSha256 = null; Details = "";
        OnPropertyChanged(nameof(Evidence)); OnPropertyChanged(nameof(VerifiedTemplateSha256));
    }
    private static bool IsHash(string? value, int length) => value is not null && value.Length == length
        && value.All(c => c is >= '0' and <= '9' or >= 'a' and <= 'f');
}
