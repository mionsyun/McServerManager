using McServerManager.Utilities;
using McServerManager.ViewModels;
using System.Windows;

namespace McServerManager.Views;

public partial class GuidePickerWindow : Window
{
    public GuideType? SelectedGuide { get; private set; }

    public GuidePickerWindow()
    {
        InitializeComponent();
        DataContext = new GuidePickerViewModel(SelectGuide);
    }

    private void SelectGuide(GuideType type)
    {
        SelectedGuide = type;
        DialogResult = true;
        Close();
    }
}
