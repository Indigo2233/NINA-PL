using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;

namespace NINA.PL.WPF.Converters;

/// <summary>Maps true/false to accent vs muted brush (parameter: "warning" uses red accent).</summary>
public sealed class BoolToColorConverter : IValueConverter
{
    private static readonly SolidColorBrush Accent = new(Color.FromRgb(0x4F, 0xC3, 0xF7));
    private static readonly SolidColorBrush Muted = new(Color.FromRgb(0x98, 0x98, 0xA8));
    private static readonly SolidColorBrush Warning = new(Color.FromRgb(0xFF, 0x6B, 0x6B));

    static BoolToColorConverter()
    {
        Accent.Freeze();
        Muted.Freeze();
        Warning.Freeze();
    }

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        bool flag = value is true;
        if (parameter is string s && s.Equals("invert", StringComparison.OrdinalIgnoreCase))
            flag = !flag;

        if (!flag)
            return Muted;

        if (parameter is string mode && mode.Equals("warning", StringComparison.OrdinalIgnoreCase))
            return Warning;

        return Accent;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
