using System.Windows;
using System.Windows.Media;
using Microsoft.Win32;

namespace MorseTrainer.Services;

public enum AppTheme
{
    System,
    Dark,
    Light
}

public static class ThemeService
{
    private static readonly IReadOnlyDictionary<string, string> DarkPalette = new Dictionary<string, string>
    {
        ["WindowBrush"] = "#10121A",
        ["CardBrush"] = "#191C28",
        ["CardAltBrush"] = "#202433",
        ["PrimaryBrush"] = "#75E6B1",
        ["PrimaryDarkBrush"] = "#0F3326",
        ["TextBrush"] = "#F4F6FA",
        ["MutedTextBrush"] = "#9DA6B8",
        ["BorderBrush"] = "#303548",
        ["DangerBrush"] = "#FF7B8A"
    };

    private static readonly IReadOnlyDictionary<string, string> LightPalette = new Dictionary<string, string>
    {
        ["WindowBrush"] = "#F3F6F9",
        ["CardBrush"] = "#FFFFFF",
        ["CardAltBrush"] = "#EDF2F6",
        ["PrimaryBrush"] = "#20B77A",
        ["PrimaryDarkBrush"] = "#DDF7EC",
        ["TextBrush"] = "#17202B",
        ["MutedTextBrush"] = "#647184",
        ["BorderBrush"] = "#D5DDE6",
        ["DangerBrush"] = "#D84558"
    };

    public static void Apply(AppTheme selectedTheme)
    {
        var resolvedTheme = selectedTheme == AppTheme.System
            ? (IsWindowsLightTheme() ? AppTheme.Light : AppTheme.Dark)
            : selectedTheme;
        var palette = resolvedTheme == AppTheme.Light ? LightPalette : DarkPalette;

        foreach (var (key, colorValue) in palette)
        {
            var color = (Color)ColorConverter.ConvertFromString(colorValue);
            Application.Current.Resources[key] = new SolidColorBrush(color);
        }
    }

    private static bool IsWindowsLightTheme()
    {
        try
        {
            var value = Registry.GetValue(
                @"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize",
                "AppsUseLightTheme",
                0);
            return value is int intValue && intValue > 0;
        }
        catch
        {
            return false;
        }
    }
}
