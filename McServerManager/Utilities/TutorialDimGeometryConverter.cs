using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;

namespace McServerManager.Utilities;

public sealed class TutorialDimGeometryConverter : IMultiValueConverter
{
    public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
    {
        if (values.Length < 3)
        {
            return Geometry.Empty;
        }

        var width = values[0] as double? ?? 0;
        var height = values[1] as double? ?? 0;
        var highlight = values[2] is Rect rect ? rect : Rect.Empty;
        var radius = values.Length > 3 && values[3] is double r ? r : 8;

        if (width <= 0 || height <= 0)
        {
            return Geometry.Empty;
        }

        var fullRect = new RectangleGeometry(new Rect(0, 0, width, height));
        if (highlight.Width <= 0 || highlight.Height <= 0)
        {
            return fullRect;
        }

        var holeRect = new RectangleGeometry(highlight, radius, radius);
        return new CombinedGeometry(GeometryCombineMode.Exclude, fullRect, holeRect);
    }

    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}
