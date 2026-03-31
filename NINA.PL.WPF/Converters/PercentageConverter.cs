using System.Globalization;
using System.Windows.Data;

namespace NINA.PL.WPF.Converters;

/// <summary>Maps a fraction in [0,1] to a percentage label like "42%".</summary>
public sealed class PercentageConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        double d = value switch
        {
            double x => x,
            float f => f,
            int i => i,
            _ => double.NaN,
        };

        if (double.IsNaN(d))
            return "—";

        string fmt = parameter as string ?? "F0";
        return (d * 100.0).ToString(fmt, culture) + "%";
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is string s && s.TrimEnd('%').Trim() is { Length: > 0 } num
            && double.TryParse(num, NumberStyles.Float, culture, out double p))
            return p / 100.0;
        return 0.0;
    }
}
