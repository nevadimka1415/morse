namespace MorseTrainer.Mobile.Services;

/// <summary>
/// Кнопки «один из нескольких» вместо выпадающего списка (режим тренировки, набор символов):
/// выбранная — зелёная, остальные в обычном стиле, так выбор виден сразу.
/// </summary>
public static class ChoiceButtons
{
    public static void Highlight(IReadOnlyList<Button> buttons, int selected)
    {
        for (var index = 0; index < buttons.Count; index++)
        {
            if (index == selected)
            {
                buttons[index].BackgroundColor = (Color)Application.Current!.Resources["Primary"];
                buttons[index].TextColor = Color.FromArgb("#06231C");
            }
            else
            {
                buttons[index].ClearValue(Button.BackgroundColorProperty);
                buttons[index].ClearValue(Button.TextColorProperty);
            }
        }
    }

    /// <summary>Набор символов «Обучения» и «На слух»: 0 русские, 1 латинские, 2 русские и латинские, 3 цифры.</summary>
    public static int LoadAlphabet(string key) => Math.Clamp(Preferences.Default.Get(key, 0), 0, 3);

    public static void SaveAlphabet(string key, int index) => Preferences.Default.Set(key, Math.Clamp(index, 0, 3));
}
