using Microsoft.Maui.Controls.Shapes;

namespace MorseTrainer.Mobile.Controls;

/// <summary>
/// Значок приложения «· — —» (буква В), как иконка на рабочем столе: бирюзовый градиент и знак в пропорциях
/// Resources/AppIcon/appiconfg.svg. Нажатие — событие Tapped (в «Настройках» — пасхалка).
/// </summary>
public sealed class LogoMark : ContentView
{
    private const double Size = 44;

    public LogoMark()
    {
        // Лаунчер показывает центральные 72 из 108 dp адаптивной иконки — 341 из 512 единиц SVG: знак того же размера
        var k = Size / (512.0 * 72 / 108);
        var mark = new HorizontalStackLayout { Spacing = 13 * k, HorizontalOptions = LayoutOptions.Center, VerticalOptions = LayoutOptions.Center };
        mark.Add(new Ellipse { WidthRequest = 44 * k, HeightRequest = 44 * k, Fill = new SolidColorBrush(Color.FromArgb("#ECFBF4")), VerticalOptions = LayoutOptions.Center });
        for (var dash = 0; dash < 2; dash++)
        {
            mark.Add(new BoxView { WidthRequest = 95 * k, HeightRequest = 38 * k, CornerRadius = 19 * k, Color = Color.FromArgb("#76E5BD"), VerticalOptions = LayoutOptions.Center });
        }

        Content = new Border
        {
            WidthRequest = Size,
            HeightRequest = Size,
            // Общий стиль Border (карточки) даёт Padding 16 — в плитке 44 dp знак обрезался до точки и половины тире
            Padding = 0,
            StrokeThickness = 0,
            StrokeShape = new RoundRectangle { CornerRadius = Size * 0.24 },
            Background = new LinearGradientBrush(new GradientStopCollection
            {
                new GradientStop(Color.FromArgb("#05B3AD"), 0),
                new GradientStop(Color.FromArgb("#067F7D"), 0.5f),
                new GradientStop(Color.FromArgb("#02464A"), 1)
            }, new Point(0, 0), new Point(1, 1)),
            Content = mark
        };
        var tap = new TapGestureRecognizer();
        tap.Tapped += (_, _) => Tapped?.Invoke(this, EventArgs.Empty);
        GestureRecognizers.Add(tap);
        SemanticProperties.SetDescription(this, "Morse Trainer");
    }

    public event EventHandler? Tapped;
}
