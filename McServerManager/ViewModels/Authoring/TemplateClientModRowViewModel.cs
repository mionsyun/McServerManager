using McServerManager.Models.Authoring;

namespace McServerManager.ViewModels.Authoring;

public sealed class TemplateClientModRowViewModel : AuthoringSectionViewModel
{
    private string _projectId, _versionId, _name, _version, _clientSide, _note;
    public static IReadOnlyList<string> ClientSideOptions { get; } = Array.AsReadOnly(new[] { "required", "optional" });
    internal TemplateClientModRowViewModel(TemplateAuthoringClientModDraft? draft, Func<bool> canEdit, Action changed)
        : base(canEdit, changed)
    {
        _projectId = draft?.ProjectId ?? "";
        _versionId = draft?.VersionId ?? "";
        _name = draft?.Name ?? "";
        _version = draft?.Version ?? "";
        _clientSide = draft?.ClientSide ?? "required";
        _note = draft?.Note ?? "";
    }
    public string Provider => "modrinth";
    public string ProjectId { get => _projectId; set => SetInput(ref _projectId, value ?? ""); }
    public string VersionId { get => _versionId; set => SetInput(ref _versionId, value ?? ""); }
    public string Name { get => _name; set => SetInput(ref _name, value ?? ""); }
    public string Version { get => _version; set => SetInput(ref _version, value ?? ""); }
    public string ClientSide { get => _clientSide; set => SetInput(ref _clientSide, value ?? ""); }
    public string Note { get => _note; set => SetInput(ref _note, value ?? ""); }
    internal TemplateAuthoringClientModDraft ToDraft() => new()
    {
        Provider = Provider, ProjectId = ProjectId, VersionId = VersionId, Name = Name,
        Version = Version, ClientSide = ClientSide, Note = TemplateRuntimeViewModel.Optional(Note)
    };
}
