using System.Windows;
using McServerManager.ViewModels;

namespace McServerManager.Views;

public partial class WorldMapWindow : Window
{
    public WorldMapWindow(WorldMapViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
        Loaded += async (_, _) => await viewModel.LoadMapAsync();
    }
}
