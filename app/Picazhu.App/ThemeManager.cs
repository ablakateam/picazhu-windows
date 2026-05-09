using System.Windows;

namespace Picazhu.App;

public static class ThemeManager
{
    private const string DarkThemePath = "Themes/DarkTheme.xaml";
    private const string LightThemePath = "Themes/LightTheme.xaml";

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
            var source = dictionaries[index].Source?.OriginalString;
            if (string.Equals(source, DarkThemePath, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(source, LightThemePath, StringComparison.OrdinalIgnoreCase))
            {
                dictionaries.RemoveAt(index);
            }
        }

        dictionaries.Add(new ResourceDictionary { Source = new Uri(dictionaryPath, UriKind.Relative) });
    }
}
