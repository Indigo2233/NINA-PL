using System.Globalization;
using System.Windows.Data;

namespace NINA.PL.WPF.Converters;

/// <summary>True when all non-null values are reference-equal to the first value.</summary>
public sealed class ReferenceEqualityMultiConverter : IMultiValueConverter
{
    public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
    {
        if (values.Length < 2)
            return false;
        object? a = values[0];
        for (int i = 1; i < values.Length; i++)
        {
            if (!ReferenceEquals(a, values[i]))
                return false;
        }
        return true;
    }

    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
