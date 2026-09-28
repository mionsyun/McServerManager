using System.Diagnostics;
using System.Windows;
using System.Windows.Navigation;

namespace McServerManager.Views;

public partial class NewServerWindow : Window
{
    public NewServerWindow()
    {
        InitializeComponent();
    }

    /// <summary>利用規約リンクを既定のブラウザで開く（https のみ）。</summary>
    private void OnRequestNavigate(object sender, RequestNavigateEventArgs e)
    {
        if (e.Uri.Scheme == Uri.UriSchemeHttps)
        {
            Process.Start(new ProcessStartInfo(e.Uri.AbsoluteUri) { UseShellExecute = true });
        }

        e.Handled = true;
    }
}
