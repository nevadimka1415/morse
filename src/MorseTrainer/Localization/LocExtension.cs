using System.Windows.Markup;

namespace MorseTrainer.Localization;

/// <summary>{loc:Loc 'русский текст'} в XAML: подставляет перевод, если выбран английский.</summary>
[MarkupExtensionReturnType(typeof(string))]
public sealed class LocExtension : MarkupExtension
{
    public LocExtension()
    {
    }

    public LocExtension(string key)
    {
        Key = key;
    }

    [ConstructorArgument("key")]
    public string Key { get; set; } = string.Empty;

    public override object ProvideValue(IServiceProvider serviceProvider) => Texts.T(Key);
}
