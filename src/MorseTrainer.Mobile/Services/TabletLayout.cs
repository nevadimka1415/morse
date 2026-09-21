namespace MorseTrainer.Mobile.Services;

/// <summary>
/// Планшеты и альбомная ориентация: контент не растягивается на всю ширину,
/// а центрируется полосой не шире ContentWidth. На телефонах отступы обычные.
/// </summary>
public static class TabletLayout
{
    public const double ContentWidth = 720;
    public const double SideMargin = 16;

    public static Thickness PaddingFor(double pageWidth, double vertical = 16)
    {
        var side = pageWidth > ContentWidth + SideMargin * 2
            ? Math.Floor((pageWidth - ContentWidth) / 2)
            : SideMargin;
        return new Thickness(side, vertical);
    }

    /// <summary>Число колонок карточек обучения по ширине экрана.</summary>
    public static int LearningColumnsFor(double pageWidth) => pageWidth switch
    {
        > 1000 => 4,
        > 640 => 3,
        _ => 2
    };
}
