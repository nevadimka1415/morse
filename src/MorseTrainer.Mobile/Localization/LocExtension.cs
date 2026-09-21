using Microsoft.Maui.Controls.Xaml;
using MorseTrainer.Localization;

namespace MorseTrainer.Mobile.Localization;

/// <summary>{loc:Loc 'русский текст'} в XAML телефона: подставляет перевод, если выбран английский.</summary>
[ContentProperty(nameof(Key))]
public sealed class LocExtension : IMarkupExtension<string>
{
    public string Key { get; set; } = string.Empty;

    public string ProvideValue(IServiceProvider serviceProvider) => Texts.T(Key);

    object IMarkupExtension.ProvideValue(IServiceProvider serviceProvider) => ProvideValue(serviceProvider);
}
