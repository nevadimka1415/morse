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

    // В MAUI 9+ свойство MainPage устарело: корневая страница задаётся через окно.
    // Своя область сервисов на каждое окно: Android пересоздаёт активность (смена шрифта, языка, системных ресурсов),
    // MAUI вызывает CreateWindow заново — и оболочка со страницами должна быть новой. Старые страницы привязаны к
    // закрытому контексту прежнего окна: при переключении вкладки было ObjectDisposedException (IServiceProvider).
    protected override Window CreateWindow(IActivationState? activationState)
    {
        var scope = _services.CreateScope();
        var window = new Window(scope.ServiceProvider.GetRequiredService<AppShell>());
        window.Destroying += (_, _) => scope.Dispose();
        return window;
    }
}
