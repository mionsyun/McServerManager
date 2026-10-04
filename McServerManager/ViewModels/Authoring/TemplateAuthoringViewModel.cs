using System.Collections.ObjectModel;
using McServerManager.Models.Authoring;
using McServerManager.Models.Editions;
using McServerManager.Models.Templates;
using McServerManager.Services.Authoring;
using McServerManager.Services.Editions;
using McServerManager.Utilities;

namespace McServerManager.ViewModels.Authoring;

/// <summary>Offline form/review state. File dialogs and asynchronous disk I/O belong to the window.</summary>
public sealed partial class TemplateAuthoringViewModel : ObservableObject
{
    private readonly ITemplateAuthoringService _service;
    private readonly IEditionPolicy _policy;
    private readonly ObservableCollection<TemplateClientModRowViewModel> _clientMods = [];
    private TemplateAuthoringSession? _session;
    private TemplateAuthoringPreparedExport? _prepared;
    private byte[]? _exportSnapshot;
    private bool _editingCompleted;
    private string _name = "", _description = "", _clientName = "", _sourceFileName = "";
    private string _summary = "新しく作るか、編集するテンプレートを選んでください。", _details = "";
    private bool _hasClientDefinition, _isDirty, _isReviewing, _acknowledgedLimitations, _hasExportBytes;
    private long _revision;

    public TemplateAuthoringViewModel(ITemplateAuthoringService service, IEditionPolicy policy)
    {
        _service = service ?? throw new ArgumentNullException(nameof(service));
        _policy = policy ?? throw new ArgumentNullException(nameof(policy));
        ClientMods = new ReadOnlyObservableCollection<TemplateClientModRowViewModel>(_clientMods);
        ServerRuntime = NewServerRuntime(new TemplateRuntime { Type = "vanilla", MinecraftVersion = "" });
        Settings = new TemplateSettingsViewModel(new TemplateSettings(), () => CanEdit, DraftChanged);
        ClientRuntime = new TemplateClientRuntimeViewModel(null, () => CanEdit, DraftChanged);
    }

    public static IReadOnlyList<string> ServerTypes { get; } = Array.AsReadOnly(new[] { "vanilla", "paper", "fabric", "forge" });
    public static IReadOnlyList<string> ClientLoaders { get; } = Array.AsReadOnly(new[] { "fabric", "forge" });
    public static IReadOnlyList<string> Difficulties { get; } = Array.AsReadOnly(new[] { "", "peaceful", "easy", "normal", "hard" });
    public static IReadOnlyList<string> Gamemodes { get; } = Array.AsReadOnly(new[] { "", "survival", "creative", "adventure", "spectator" });
    public string Name { get => _name; set => SetInput(ref _name, value ?? "", nameof(Name)); }
    public string Description { get => _description; set => SetInput(ref _description, value ?? "", nameof(Description)); }
    public string ClientName { get => _clientName; set => SetInput(ref _clientName, value ?? "", nameof(ClientName)); }
    public bool HasClientDefinition { get => _hasClientDefinition; set => SetInput(ref _hasClientDefinition, value, nameof(HasClientDefinition)); }
    public TemplateRuntimeViewModel ServerRuntime { get; private set; }
    public TemplateSettingsViewModel Settings { get; private set; }
    public TemplateClientRuntimeViewModel ClientRuntime { get; private set; }
    public ReadOnlyObservableCollection<TemplateClientModRowViewModel> ClientMods { get; }
    public string Summary { get => _summary; private set => SetProperty(ref _summary, value); }
    public string Details { get => _details; private set => SetProperty(ref _details, value); }
    public string SourceFileName => _sourceFileName;
    public string? OriginalManifestSha256 => _session?.ImportedManifestSha256;
    public string? OutputManifestSha256 => _prepared?.ManifestSha256;
    public string ServerAddonsSummary => DescribeServerAddons();
    public bool IsServerRuntimeLocked => _session?.IsServerRuntimeLocked == true;
    public bool HasDraft => _session is not null;
    public bool IsDirty { get => _isDirty; private set => SetProperty(ref _isDirty, value); }
    public bool IsReviewing => _isReviewing;
    public long ReviewRevision => _revision;
    public bool CanCreate => _policy.Allows(EditionCapability.TemplateCreate);
    public bool CanLoad => _policy.Allows(EditionCapability.TemplateEdit);
    public bool CanEdit => HasDraft && !_editingCompleted && _policy.Allows(_session!.IsEditing ? EditionCapability.TemplateEdit : EditionCapability.TemplateCreate);
    public bool CanEditServerRuntime => CanEdit && !IsServerRuntimeLocked;
    public bool CanValidate => CanEdit && _policy.Allows(EditionCapability.TemplateExport);
    public bool CanAcknowledge => CanValidate && IsReviewing && _prepared is not null;
    public bool CanExport => CanAcknowledge && AcknowledgedLimitations;
    public bool CanAddClientMod => CanEdit && HasClientDefinition && ClientMods.Count < 256;
    public bool AcknowledgedLimitations
    {
        get => _acknowledgedLimitations;
        set
        {
            if (value && !CanAcknowledge) return;
            if (!SetProperty(ref _acknowledgedLimitations, value)) return;
            _hasExportBytes = false;
            ++_revision;
            NotifyActions();
        }
    }

    public bool CreateNew(bool discardChanges = false)
    {
        if (!CanCreate) return DenyEdition();
        if (!MayReplace(discardChanges)) return false;
        InvalidateReview();
        try
        {
            var result = _service.CreateNew();
            if (!result.IsValid || result.Session is null || result.Draft is null) return ReportIssues(result.Issues);
            ApplyDraft(result.Session, result.Draft, "");
            Summary = "1. サーバー設定を入力してください。参加者用の構成は別に指定できます。";
            return true;
        }
        catch (InvalidOperationException) { return DenyEdition(); }
    }

    public bool LoadForEdit(ReadOnlyMemory<byte> bytes, string sourceFileName, bool discardChanges = false)
    {
        if (!CanLoad) return DenyEdition();
        if (!MayReplace(discardChanges)) return false;
        InvalidateReview();
        try
        {
            var result = _service.OpenForEdit(bytes);
            if (!result.IsValid || result.Session is null || result.Draft is null) return ReportIssues(result.Issues);
            ApplyDraft(result.Session, result.Draft, FileNameOnly(sourceFileName));
            Summary = "1. 入力形式を確認して読み込みました。編集後に内容を確認し、別のファイルとして保存してください。";
            return true;
        }
        catch (InvalidOperationException) { return DenyEdition(); }
    }

    public bool AddClientMod()
    {
        if (!CanEdit) return DenyEdition();
        if (!CanAddClientMod)
        { Summary = HasClientDefinition ? "参加者用 MOD は最大256件です。" : "参加者用の構成を有効にしてください。"; return false; }
        _clientMods.Add(new TemplateClientModRowViewModel(null, () => CanEdit, DraftChanged));
        DraftChanged();
        return true;
    }

    public bool RemoveClientMod(TemplateClientModRowViewModel row)
    {
        if (!CanEdit) return DenyEdition();
        if (!_clientMods.Remove(row)) return false;
        row.Detach();
        DraftChanged();
        return true;
    }

    public bool Reset(bool discardChanges = false)
    {
        if (!MayReplace(discardChanges)) return false;
        InvalidateReview();
        DetachSections();
        _session = null;
        _editingCompleted = false;
        _name = _description = _clientName = _sourceFileName = "";
        _hasClientDefinition = false;
        _clientMods.Clear();
        ServerRuntime = NewServerRuntime(new TemplateRuntime { Type = "vanilla", MinecraftVersion = "" });
        Settings = new TemplateSettingsViewModel(new TemplateSettings(), () => CanEdit, DraftChanged);
        ClientRuntime = new TemplateClientRuntimeViewModel(null, () => CanEdit, DraftChanged);
        IsDirty = false;
        Summary = "新しく作るか、編集するテンプレートを選んでください。";
        NotifyDraft();
        return true;
    }

    public void BackToEditor()
    {
        InvalidateReview();
        Summary = "入力内容は残っています。編集後はもう一度内容を確認してください。";
    }
    public void Cancel()
    {
        InvalidateReview();
        Summary = "操作を取り消しました。入力内容は残っています。保存するにはもう一度内容を確認してください。";
    }
    public void ReportOpenCanceled() => Summary = "読み込みを取り消しました。入力内容は変わっていません。";
    public void ReportOpenFailure()
    {
        InvalidateReview();
        Summary = "ファイルを開けませんでした。入力内容は残っています。読み取れるテンプレートを選び直してください。";
    }

    private bool MayReplace(bool discardChanges)
    {
        if (!IsDirty || discardChanges) return true;
        Summary = "未保存の変更があります。破棄してよいか確認してから操作してください。";
        return false;
    }
    private bool DenyEdition()
    {
        InvalidateReview();
        Summary = "テンプレートの作成・編集・書き出しには MaiPilot Pro が必要です。";
        return false;
    }
    private void SetInput<T>(ref T field, T value, string property)
    {
        if (CanEdit && SetProperty(ref field, value, property)) DraftChanged();
    }
    private void DraftChanged()
    {
        IsDirty = true;
        InvalidateReview();
        Summary = "入力内容を変更しました。書き出す前に内容を確認してください。";
    }
    private void InvalidateReview()
    {
        ++_revision;
        _prepared = null;
        _exportSnapshot = null;
        _isReviewing = _acknowledgedLimitations = _hasExportBytes = false;
        Details = "";
        OnPropertyChanged(nameof(AcknowledgedLimitations));
        NotifyActions();
    }
    private TemplateRuntimeViewModel NewServerRuntime(TemplateRuntime runtime) =>
        new(runtime, IsServerRuntimeLocked, () => CanEdit, DraftChanged);
    private void DetachSections()
    {
        ServerRuntime.Detach(); Settings.Detach(); ClientRuntime.Detach();
        foreach (var row in _clientMods) row.Detach();
    }
    private void ApplyDraft(TemplateAuthoringSession session, TemplateAuthoringDraft draft, string sourceName)
    {
        DetachSections();
        _session = session;
        _editingCompleted = false;
        _sourceFileName = sourceName;
        _name = draft.Name;
        _description = draft.Description;
        _hasClientDefinition = draft.ClientDefinition is not null;
        _clientName = draft.ClientDefinition?.Name ?? "";
        ServerRuntime = NewServerRuntime(draft.Runtime);
        Settings = new TemplateSettingsViewModel(draft.Settings, () => CanEdit, DraftChanged);
        ClientRuntime = new TemplateClientRuntimeViewModel(draft.ClientDefinition, () => CanEdit, DraftChanged);
        _clientMods.Clear();
        foreach (var row in draft.ClientDefinition?.Mods ?? [])
            _clientMods.Add(new TemplateClientModRowViewModel(row, () => CanEdit, DraftChanged));
        IsDirty = false;
        NotifyDraft();
    }
    private void NotifyDraft()
    {
        foreach (var name in new[] { nameof(Name), nameof(Description), nameof(ClientName), nameof(HasClientDefinition),
            nameof(ServerRuntime), nameof(Settings), nameof(ClientRuntime), nameof(SourceFileName), nameof(OriginalManifestSha256),
            nameof(ServerAddonsSummary), nameof(IsServerRuntimeLocked), nameof(HasDraft) }) OnPropertyChanged(name);
        NotifyActions();
    }
    private void NotifyActions()
    {
        foreach (var name in new[] { nameof(CanCreate), nameof(CanLoad), nameof(CanEdit), nameof(CanEditServerRuntime),
            nameof(CanValidate), nameof(CanAddClientMod), nameof(CanAcknowledge), nameof(CanExport), nameof(IsReviewing),
            nameof(ReviewRevision), nameof(OutputManifestSha256) }) OnPropertyChanged(name);
    }
    private static string FileNameOnly(string value) => System.IO.Path.GetFileName(value.Replace('\\', '/'));
}
