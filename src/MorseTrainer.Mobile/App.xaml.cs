using Microsoft.Extensions.DependencyInjection;

namespace MorseTrainer.Mobile;

public partial class App : Application
{
    private readonly IServiceProvider _services;

    // Оболочка со страницами берётся из контейнера только в CreateWindow: если принять AppShell в конструктор,
    // DI создаст страницы раньше InitializeComponent, их XAML не найдёт ресурсы App (PrimaryDark и др.)
    // и приложение упадёт при запуске с XamlParseException
    public App(IServiceProvider services)
    {
        InitializeComponent();
        _services = services;
    }

    // В MAUI 9+ свойство MainPage устарело: корневая страница задаётся через окно
    protected override Window CreateWindow(IActivationState? activationState) => new Window(_services.GetRequiredService<AppShell>());
}
