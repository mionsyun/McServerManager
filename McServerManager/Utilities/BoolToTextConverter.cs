using System.Globalization;
using System.Windows.Data;

namespace McServerManager.Utilities;

/// <summary>
/// bool を文字列に変換する。ConverterParameter は "true のときの文字列|false のときの文字列"。
/// 例: ConverterParameter="サーバー名|MOTD"
/// </summary>
[ValueConversion(typeof(bool), typeof(string))]
public sealed class BoolToTextConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var parts = (parameter as string ?? string.Empty).Split('|', 2);
        var whenTrue = parts[0];
        var whenFalse = parts.Length > 1 ? parts[1] : string.Empty;
        return value is true ? whenTrue : whenFalse;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
