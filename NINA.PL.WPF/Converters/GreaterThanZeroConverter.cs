using System.Globalization;
using System.Windows.Data;

namespace NINA.PL.WPF.Converters;

[ValueConversion(typeof(int), typeof(bool))]
public sealed class GreaterThanZeroConverter : IValueConverter
{
    public static readonly GreaterThanZeroConverter Instance = new();

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        bool result;
        if (value is int i)
            result = i > 0;
        else if (value is double d)
            result = d > 0;
        else
            result = false;

        if (parameter is string s && s.Equals("invert", StringComparison.OrdinalIgnoreCase))
            result = !result;

        if (targetType == typeof(System.Windows.Visibility))
            return result ? System.Windows.Visibility.Visible : System.Windows.Visibility.Collapsed;

        return result;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
