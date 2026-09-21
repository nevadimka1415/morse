namespace MorseTrainer.Mobile;

public partial class App : Application
{
    private readonly AppShell _shell;

    public App(AppShell shell)
    {
        InitializeComponent();
        _shell = shell;
    }

    // В MAUI 9+ свойство MainPage устарело: корневая страница задаётся через окно
    protected override Window CreateWindow(IActivationState? activationState) => new Window(_shell);
}
