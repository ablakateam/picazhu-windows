using System.Windows;

namespace Picazhu.App;

public static class ThemeManager
{
    private const string DarkThemePath = "Themes/DarkTheme.xaml";
    private const string LightThemePath = "Themes/LightTheme.xaml";
    private const string NormalizedDarkThemePath = "Themes/DarkTheme.xaml";
    private const string NormalizedLightThemePath = "Themes/LightTheme.xaml";

    public static void ApplyTheme(string? themeMode)
    {
        var app = Application.Current;
        if (app is null)
        {
            return;
        }

        var dictionaryPath = string.Equals(themeMode, "Light", StringComparison.OrdinalIgnoreCase)
            ? LightThemePath
            : DarkThemePath;

        var dictionaries = app.Resources.MergedDictionaries;
        for (var index = dictionaries.Count - 1; index >= 0; index--)
        {
            if (IsThemeDictionary(dictionaries[index]))
            {
                dictionaries.RemoveAt(index);
            }
        }

        dictionaries.Add(new ResourceDictionary { Source = new Uri(dictionaryPath, UriKind.Relative) });
    }

    private static bool IsThemeDictionary(ResourceDictionary dictionary)
    {
        var source = dictionary.Source?.OriginalString;
        if (string.IsNullOrWhiteSpace(source))
        {
            return false;
        }

        var normalized = source.Replace('\\', '/');
        return normalized.EndsWith(NormalizedDarkThemePath, StringComparison.OrdinalIgnoreCase) ||
               normalized.EndsWith(NormalizedLightThemePath, StringComparison.OrdinalIgnoreCase);
    }
}
