using System.Globalization;
using System.Windows.Data;

namespace NINA.PL.WPF.Converters;

public sealed class FocusMetricBarHeightMultiConverter : IMultiValueConverter
{
    public double MaxBarHeight { get; set; } = 140;

    public object Convert(object[] values, Type targetType, object? parameter, CultureInfo culture)
    {
        double metric = values.ElementAtOrDefault(0) is double d ? d : 0;
        double max = values.ElementAtOrDefault(1) is double m && m > 0 ? m : 1;
        double ratio = Math.Clamp(metric / max, 0, 1);
        return Math.Max(4, ratio * MaxBarHeight);
    }

    public object[] ConvertBack(object value, Type[] targetTypes, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
