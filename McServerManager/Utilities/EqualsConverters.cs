using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace McServerManager.Utilities;

/// <summary>
/// value と ConverterParameter（文字列比較・大文字小文字無視）が一致すれば true。
/// サイドバーのナビ選択状態のハイライトなどに使用。
/// </summary>
public sealed class EqualsToBooleanConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return string.Equals(value?.ToString(), parameter?.ToString(), StringComparison.OrdinalIgnoreCase);
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        // ToggleButton/RadioButton から CurrentView を更新する用途。
        // true のときだけ parameter を返し、false は Binding.DoNothing。
        return value is true && parameter is not null ? parameter : System.Windows.Data.Binding.DoNothing;
    }
}

/// <summary>
/// value と ConverterParameter が一致すれば Visible、そうでなければ Collapsed。
/// CurrentView に応じた画面切り替えに使用。
/// </summary>
public sealed class EqualsToVisibilityConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return string.Equals(value?.ToString(), parameter?.ToString(), StringComparison.OrdinalIgnoreCase)
            ? Visibility.Visible
            : Visibility.Collapsed;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => System.Windows.Data.Binding.DoNothing;
}

/// <summary>
/// オブジェクトが null でなければ Visible、null なら Collapsed。
/// ConverterParameter="invert" で反転。
/// </summary>
public sealed class NotNullToVisibilityConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var visible = value is not null;
        if (string.Equals(parameter?.ToString(), "invert", StringComparison.OrdinalIgnoreCase))
            visible = !visible;
        return visible ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => System.Windows.Data.Binding.DoNothing;
}

/// <summary>
/// bool を反転して Visibility に変換（true→Collapsed, false→Visible）。
/// 「未選択時のプレースホルダー」などに使用。
/// </summary>
public sealed class InverseBooleanToVisibilityConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is true ? Visibility.Collapsed : Visibility.Visible;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => System.Windows.Data.Binding.DoNothing;
}
