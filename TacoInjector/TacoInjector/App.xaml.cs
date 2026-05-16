using TacoInjector.Services.Windows;

namespace TacoInjector;

public partial class App
{
    public App()
    {
        InitializeComponent();
    }

    protected override Window CreateWindow(IActivationState? activationState)
    {
        return new Window(new MainPage())
        {
            Title = "TacoInjector",
            Width = WindowChrome.WindowWidth,
            Height = WindowChrome.WindowHeight
        };
    }
}
