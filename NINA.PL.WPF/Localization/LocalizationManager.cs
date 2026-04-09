using System;
using System.Windows;

namespace NINA.PL.WPF.Localization;

public static class LocalizationManager
{
    private static readonly Uri EnUri = new("pack://application:,,,/Strings/Strings.en.xaml");
    private static readonly Uri ZhUri = new("pack://application:,,,/Strings/Strings.zh.xaml");

    private static ResourceDictionary? _current;

    public static void SetLanguage(string lang)
    {
        var app = Application.Current;
        if (app is null) return;

        if (_current is not null)
            app.Resources.MergedDictionaries.Remove(_current);

        var uri = lang.StartsWith("zh", StringComparison.OrdinalIgnoreCase) ? ZhUri : EnUri;
        _current = new ResourceDictionary { Source = uri };
        app.Resources.MergedDictionaries.Add(_current);
    }
}
