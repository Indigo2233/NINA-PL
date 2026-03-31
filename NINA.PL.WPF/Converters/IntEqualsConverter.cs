using System.Globalization;
using System.Windows.Data;

namespace NINA.PL.WPF.Converters;

[ValueConversion(typeof(int), typeof(bool))]
public sealed class IntEqualsConverter : IValueConverter
{
    public static readonly IntEqualsConverter Instance = new();

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is int v && parameter is string s && int.TryParse(s, out int target))
            return v == target;
        return false;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is true && parameter is string s && int.TryParse(s, out int target))
            return target;
        return Binding.DoNothing;
    }
}
