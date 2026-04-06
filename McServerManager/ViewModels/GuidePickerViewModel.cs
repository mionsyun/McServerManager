using McServerManager.Utilities;

namespace McServerManager.ViewModels;

public sealed class GuidePickerViewModel
{
    public GuidePickerViewModel(Action<GuideType> onSelect)
    {
        SelectInitialSetupCommand = new RelayCommand(_ => onSelect(GuideType.InitialSetup));
        SelectDistributionMapCommand = new RelayCommand(_ => onSelect(GuideType.DistributionMap));
        SelectModCommand = new RelayCommand(_ => onSelect(GuideType.Mod));
        SelectPluginCommand = new RelayCommand(_ => onSelect(GuideType.Plugin));
        SelectResourcePackCommand = new RelayCommand(_ => onSelect(GuideType.ResourcePack));
    }

    public RelayCommand SelectInitialSetupCommand { get; }
    public RelayCommand SelectDistributionMapCommand { get; }
    public RelayCommand SelectModCommand { get; }
    public RelayCommand SelectPluginCommand { get; }
    public RelayCommand SelectResourcePackCommand { get; }
}
