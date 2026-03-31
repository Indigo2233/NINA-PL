using System.Globalization;
using System.Windows.Data;

namespace NINA.PL.WPF.Converters;

[ValueConversion(typeof(int), typeof(bool))]
public sealed class GreaterThanZeroConverter : IValueConverter
{
    public static readonly GreaterThanZeroConverter Instance = new();

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is int i)
            return i > 0;
        if (value is double d)
            return d > 0;
        return false;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
