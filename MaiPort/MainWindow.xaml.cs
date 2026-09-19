using System.Collections.Specialized;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using MaiPort.ViewModels;

namespace MaiPort;

public partial class MainWindow : Window
{
    private static readonly Regex NonDigit = new(@"[^0-9]", RegexOptions.Compiled);

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

        // 起動直後にポート番号をすぐ打ち替えられるようにする。
        PortTextBox.Focus();
        PortTextBox.SelectAll();

        // ログが増えたら最新行まで自動でスクロールする。
        ((INotifyCollectionChanged)_viewModel.Log.Lines).CollectionChanged += OnLogLinesChanged;

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

    private void OnLogLinesChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (LogList.Items.Count > 0)
        {
            LogList.ScrollIntoView(LogList.Items[^1]);
        }
    }

    /// <summary>ポート番号欄に数字以外を入力させない。</summary>
    private void OnPortPreviewTextInput(object sender, TextCompositionEventArgs e)
    {
        e.Handled = NonDigit.IsMatch(e.Text);
    }

    /// <summary>貼り付けも数字だけに限定する。</summary>
    private void OnPortPasting(object sender, DataObjectPastingEventArgs e)
    {
        if (e.DataObject.GetData(typeof(string)) is not string text || NonDigit.IsMatch(text))
        {
            e.CancelCommand();
        }
    }

    /// <summary>ポート番号欄でのスペース入力を無効にする。</summary>
    private void OnPortPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Space)
        {
            e.Handled = true;
        }
    }
}
