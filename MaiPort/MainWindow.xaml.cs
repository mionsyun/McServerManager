using System.Windows;
using MaiPort.ViewModels;

namespace MaiPort;

public partial class MainWindow : Window
{
    private readonly MainViewModel _viewModel;

    public MainWindow(MainViewModel viewModel)
    {
        _viewModel = viewModel ?? throw new ArgumentNullException(nameof(viewModel));
        InitializeComponent();
        DataContext = viewModel;
        Loaded += OnLoaded;
    }

    // UI イベントハンドラのため async void を使用する。
    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        Loaded -= OnLoaded;

        try
        {
            await _viewModel.InitializeAsync();
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                $"初期化中にエラーが発生しました。{Environment.NewLine}{ex.Message}",
                "MaiPort",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }
    }
}
