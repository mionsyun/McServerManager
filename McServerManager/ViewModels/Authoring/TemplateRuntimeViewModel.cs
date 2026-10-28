using McServerManager.Models.Templates;

namespace McServerManager.ViewModels.Authoring;

public sealed class TemplateRuntimeViewModel : AuthoringSectionViewModel
{
    private string _type, _minecraftVersion, _build, _loaderVersion, _installerVersion;
    internal TemplateRuntimeViewModel(TemplateRuntime runtime, bool locked, Func<bool> canEdit, Action changed)
        : base(() => !locked && canEdit(), changed)
    {
        _type = runtime.Type;
        _minecraftVersion = runtime.MinecraftVersion;
        _build = runtime.Build ?? "";
        _loaderVersion = runtime.LoaderVersion ?? "";
        _installerVersion = runtime.InstallerVersion ?? "";
        IsLocked = locked;
    }
    public bool IsLocked { get; }
    public string Type { get => _type; set => SetInput(ref _type, value ?? ""); }
    public string MinecraftVersion { get => _minecraftVersion; set => SetInput(ref _minecraftVersion, value ?? ""); }
    public string Build { get => _build; set => SetInput(ref _build, value ?? ""); }
    public string LoaderVersion { get => _loaderVersion; set => SetInput(ref _loaderVersion, value ?? ""); }
    public string InstallerVersion { get => _installerVersion; set => SetInput(ref _installerVersion, value ?? ""); }
    internal TemplateRuntime ToDraft() => new()
    {
        Type = Type, MinecraftVersion = MinecraftVersion, Build = Optional(Build),
        LoaderVersion = Optional(LoaderVersion), InstallerVersion = Optional(InstallerVersion)
    };
    internal static string? Optional(string value) => value.Length == 0 ? null : value;
}
