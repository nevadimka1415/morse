namespace MorseTrainer.Mobile.Services;

/// <summary>
/// Акцентный цвет текста по теме: тёмно-бирюзовый (PrimaryDark) на светлой, ярко-зелёный (Primary) на тёмной —
/// PrimaryDark на тёмном фоне почти не читается. В XAML то же самое — AppThemeBinding. При смене темы Android
/// пересоздаёт окно (MainActivity), поэтому цвет, выставленный из кода, берётся заново.
/// </summary>
public static class ThemeColors
{
    public static Color Accent =>
        (Color)Application.Current!.Resources[Application.Current.RequestedTheme == AppTheme.Dark ? "Primary" : "PrimaryDark"];
}
