using System.Globalization;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using FluentAssertions;

namespace Picazhu.Tests;

public sealed class ThemeContractTests
{
    private static readonly XNamespace XamlNamespace = "http://schemas.microsoft.com/winfx/2006/xaml";
    private static readonly Regex HexColorPattern = new("#[0-9A-Fa-f]{6,8}", RegexOptions.Compiled);
    private static readonly string[] ThemeDependentResources =
    [
        "WindowBackgroundBrush",
        "HeroPanelBrush",
        "WindowBrush",
        "PanelBrush",
        "PanelAltBrush",
        "PanelGlassBrush",
        "ControlBrush",
        "ControlStrongBrush",
        "ControlHoverBrush",
        "ControlPressedBrush",
        "ControlBorderBrush",
        "DividerBrush",
        "AccentBrush",
        "AccentSoftBrush",
        "AccentForegroundBrush",
        "TextBrush",
        "MutedBrush",
        "SubtleBrush",
        "SelectionBrush",
        "SelectionBorderBrush",
        "SuccessBrush",
        "WarningBrush",
        "DangerBrush",
        "MediaOverlayBrush",
        "MediaOverlayTextBrush",
        "MediaBadgeInfoBrush",
        "MediaBadgeSuccessBrush",
        "MediaBadgeDangerBrush",
        "MediaBadgeTextBrush",
        "PanelShadow",
        "CardShadow"
    ];

    [Fact]
    public void LightAndDarkThemesExposeTheSameSemanticResources()
    {
        var darkKeys = LoadResourceKeys("DarkTheme.xaml");
        var lightKeys = LoadResourceKeys("LightTheme.xaml");

        lightKeys.Should().BeEquivalentTo(darkKeys);
        darkKeys.Should().Contain(new[]
        {
            "WindowColor",
            "PanelColor",
            "SurfaceColor",
            "AccentColor",
            "AccentForegroundColor",
            "MediaOverlayColor",
            "MediaBadgeInfoColor",
            "TextColor",
            "TextMutedColor",
            "ShadowColor",
            "PanelShadowOpacity",
            "CardShadowOpacity",
            "WindowBackgroundBrush",
            "HeroPanelBrush",
            "TextBrush",
            "PanelShadow",
            "CardShadow"
        });
    }

    [Fact]
    public void LightThemeHasReadableCoreContrast()
    {
        var colors = LoadThemeColors("LightTheme.xaml");

        Contrast(colors["TextColor"], colors["PanelColor"]).Should().BeGreaterThan(12.0);
        Contrast(colors["TextColor"], colors["WindowColor"]).Should().BeGreaterThan(12.0);
        Contrast(colors["TextMutedColor"], colors["PanelColor"]).Should().BeGreaterThan(5.0);
        Contrast(colors["TextSubtleColor"], colors["PanelColor"]).Should().BeGreaterThan(3.0);
        Contrast(colors["AccentForegroundColor"], colors["AccentColor"]).Should().BeGreaterThan(4.5);
        Contrast(colors["MediaBadgeTextColor"], colors["MediaBadgeInfoColor"]).Should().BeGreaterThan(4.5);
    }

    [Fact]
    public void DarkThemeHasReadableCoreContrast()
    {
        var colors = LoadThemeColors("DarkTheme.xaml");

        Contrast(colors["TextColor"], colors["PanelColor"]).Should().BeGreaterThan(12.0);
        Contrast(colors["TextMutedColor"], colors["PanelColor"]).Should().BeGreaterThan(5.0);
        Contrast(colors["TextSubtleColor"], colors["PanelColor"]).Should().BeGreaterThan(3.0);
        Contrast(colors["AccentForegroundColor"], colors["AccentColor"]).Should().BeGreaterThan(4.5);
        Contrast(colors["MediaBadgeTextColor"], colors["MediaBadgeInfoColor"]).Should().BeGreaterThan(4.5);
    }

    [Fact]
    public void AppXamlDoesNotBypassThemeResourcesWithHardCodedColors()
    {
        var appDirectory = RepoPath("app", "Picazhu.App");
        var nonThemeXamlFiles = Directory.EnumerateFiles(appDirectory, "*.xaml", SearchOption.AllDirectories)
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}Themes{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase));

        var leaks = new List<string>();
        foreach (var path in nonThemeXamlFiles)
        {
            var lines = File.ReadAllLines(path);
            for (var i = 0; i < lines.Length; i++)
            {
                foreach (Match match in HexColorPattern.Matches(lines[i]))
                {
                    leaks.Add($"{Path.GetRelativePath(FindRepoRoot(), path)}:{i + 1}: {match.Value}");
                }
            }
        }

        leaks.Should().BeEmpty("theme-specific colors should live in theme dictionaries or semantic brushes");
    }

    [Fact]
    public void ThemeDependentResourcesUseDynamicResourceReferencesInAppXaml()
    {
        var appDirectory = RepoPath("app", "Picazhu.App");
        var nonThemeXamlFiles = Directory.EnumerateFiles(appDirectory, "*.xaml", SearchOption.AllDirectories)
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}Themes{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase));

        var staleReferences = new List<string>();
        foreach (var path in nonThemeXamlFiles)
        {
            var lines = File.ReadAllLines(path);
            for (var i = 0; i < lines.Length; i++)
            {
                foreach (var key in ThemeDependentResources)
                {
                    if (lines[i].Contains($"{{StaticResource {key}}}", StringComparison.Ordinal))
                    {
                        staleReferences.Add($"{Path.GetRelativePath(FindRepoRoot(), path)}:{i + 1}: {key}");
                    }
                }
            }
        }

        staleReferences.Should().BeEmpty("runtime theme changes require theme-owned brushes and effects to be resolved dynamically");
    }

    private static HashSet<string> LoadResourceKeys(string themeFile)
    {
        var document = XDocument.Load(RepoPath("app", "Picazhu.App", "Themes", themeFile));
        return document.Root!
            .DescendantsAndSelf()
            .Select(element => element.Attribute(XamlNamespace + "Key")?.Value)
            .Where(key => !string.IsNullOrWhiteSpace(key))
            .Select(key => key!)
            .ToHashSet(StringComparer.Ordinal);
    }

    private static Dictionary<string, RgbColor> LoadThemeColors(string themeFile)
    {
        var document = XDocument.Load(RepoPath("app", "Picazhu.App", "Themes", themeFile));
        return document.Root!
            .Descendants()
            .Where(element => element.Name.LocalName == "Color")
            .Select(element => new
            {
                Key = element.Attribute(XamlNamespace + "Key")?.Value,
                Value = element.Value
            })
            .Where(entry => !string.IsNullOrWhiteSpace(entry.Key) && !string.IsNullOrWhiteSpace(entry.Value))
            .ToDictionary(entry => entry.Key!, entry => ParseColor(entry.Value), StringComparer.Ordinal);
    }

    private static double Contrast(RgbColor foreground, RgbColor background)
    {
        var lighter = Math.Max(RelativeLuminance(foreground), RelativeLuminance(background));
        var darker = Math.Min(RelativeLuminance(foreground), RelativeLuminance(background));
        return (lighter + 0.05) / (darker + 0.05);
    }

    private static double RelativeLuminance(RgbColor color)
    {
        static double Channel(byte value)
        {
            var normalized = value / 255.0;
            return normalized <= 0.03928
                ? normalized / 12.92
                : Math.Pow((normalized + 0.055) / 1.055, 2.4);
        }

        return (0.2126 * Channel(color.R)) + (0.7152 * Channel(color.G)) + (0.0722 * Channel(color.B));
    }

    private static RgbColor ParseColor(string hex)
    {
        var value = hex.Trim().TrimStart('#');
        if (value.Length == 8)
        {
            value = value[2..];
        }

        value.Length.Should().Be(6);
        return new RgbColor(
            byte.Parse(value[..2], NumberStyles.HexNumber, CultureInfo.InvariantCulture),
            byte.Parse(value[2..4], NumberStyles.HexNumber, CultureInfo.InvariantCulture),
            byte.Parse(value[4..6], NumberStyles.HexNumber, CultureInfo.InvariantCulture));
    }

    private static string RepoPath(params string[] parts)
    {
        return Path.Combine(new[] { FindRepoRoot() }.Concat(parts).ToArray());
    }

    private static string FindRepoRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null)
        {
            if (File.Exists(Path.Combine(current.FullName, "Picazhu.sln")))
            {
                return current.FullName;
            }

            current = current.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate Picazhu.sln from the test output directory.");
    }

    private readonly record struct RgbColor(byte R, byte G, byte B);
}
