using System.Globalization;
using McServerManager.Models.Templates;

namespace McServerManager.ViewModels.Authoring;

public sealed class TemplateSettingsViewModel : AuthoringSectionViewModel
{
    private string _difficulty, _gamemode, _maxPlayers, _viewDistance, _simulationDistance, _spawnProtection;
    private bool? _pvp;
    internal TemplateSettingsViewModel(TemplateSettings settings, Func<bool> canEdit, Action changed) : base(canEdit, changed)
    {
        _difficulty = settings.Difficulty ?? "";
        _gamemode = settings.Gamemode ?? "";
        _maxPlayers = Format(settings.MaxPlayers);
        _viewDistance = Format(settings.ViewDistance);
        _simulationDistance = Format(settings.SimulationDistance);
        _spawnProtection = Format(settings.SpawnProtection);
        _pvp = settings.Pvp;
    }
    public string Difficulty { get => _difficulty; set => SetInput(ref _difficulty, value ?? ""); }
    public string Gamemode { get => _gamemode; set => SetInput(ref _gamemode, value ?? ""); }
    public string MaxPlayers { get => _maxPlayers; set => SetInput(ref _maxPlayers, value ?? ""); }
    public string ViewDistance { get => _viewDistance; set => SetInput(ref _viewDistance, value ?? ""); }
    public string SimulationDistance { get => _simulationDistance; set => SetInput(ref _simulationDistance, value ?? ""); }
    public string SpawnProtection { get => _spawnProtection; set => SetInput(ref _spawnProtection, value ?? ""); }
    public bool? Pvp { get => _pvp; set => SetInput(ref _pvp, value); }

    internal TemplateSettings? ToDraft(out string? error)
    {
        error = null;
        if (!Read(MaxPlayers, "最大人数", out var maxPlayers, ref error) ||
            !Read(ViewDistance, "描画距離", out var viewDistance, ref error) ||
            !Read(SimulationDistance, "シミュレーション距離", out var simulationDistance, ref error) ||
            !Read(SpawnProtection, "スポーン保護", out var spawnProtection, ref error)) return null;
        return new TemplateSettings
        {
            Difficulty = TemplateRuntimeViewModel.Optional(Difficulty), Gamemode = TemplateRuntimeViewModel.Optional(Gamemode),
            MaxPlayers = maxPlayers, ViewDistance = viewDistance, SimulationDistance = simulationDistance,
            SpawnProtection = spawnProtection, Pvp = Pvp
        };
    }
    private static bool Read(string text, string label, out int? value, ref string? error)
    {
        value = null;
        if (text.Length == 0) return true;
        if (int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out var number))
        { value = number; return true; }
        error = $"{label} は半角の整数で入力してください。指定しない場合は空欄にしてください。";
        return false;
    }
    private static string Format(int? value) => value?.ToString(CultureInfo.InvariantCulture) ?? "";
}
