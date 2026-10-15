using NexSync.Android.Services;

namespace NexSync.Android;

public partial class App : Application
{
    private readonly IServiceProvider _services;

    public App(IServiceProvider services)
    {
        InitializeComponent();
        _services = services;
    }

    protected override Window CreateWindow(IActivationState? activationState)
    {
        var shell = new AppShell();
        Routing.RegisterRoute("login", typeof(Pages.LoginPage));
        Routing.RegisterRoute("register", typeof(Pages.RegisterPage));
        Routing.RegisterRoute("browser", typeof(Pages.BrowserPage));
        return new Window(shell);
    }
}
