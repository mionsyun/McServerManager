using McServerManager.Models.Authoring;

namespace McServerManager.ViewModels.Authoring;

public sealed class TemplateClientRuntimeViewModel : AuthoringSectionViewModel
{
    private string _minecraftVersion, _loader, _loaderVersion, _javaVersion;
    internal TemplateClientRuntimeViewModel(TemplateAuthoringClientDraft? draft, Func<bool> canEdit, Action changed)
        : base(canEdit, changed)
    {
        _minecraftVersion = draft?.MinecraftVersion ?? "";
        _loader = draft?.Loader ?? "fabric";
        _loaderVersion = draft?.LoaderVersion ?? "";
        _javaVersion = draft?.JavaVersion ?? "";
    }
    public string MinecraftVersion { get => _minecraftVersion; set => SetInput(ref _minecraftVersion, value ?? ""); }
    public string Loader { get => _loader; set => SetInput(ref _loader, value ?? ""); }
    public string LoaderVersion { get => _loaderVersion; set => SetInput(ref _loaderVersion, value ?? ""); }
    public string JavaVersion { get => _javaVersion; set => SetInput(ref _javaVersion, value ?? ""); }
}
