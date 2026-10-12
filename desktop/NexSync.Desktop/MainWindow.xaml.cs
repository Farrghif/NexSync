using System.Windows;
using System.Windows.Controls;
using WpfUserControl = System.Windows.Controls.UserControl;
using NexSync.Desktop.Views;

namespace NexSync.Desktop;

public partial class MainWindow : System.Windows.Window
{
    private readonly DesktopServices _services;

    public MainWindow()
        : this(new DesktopServices())
    {
    }

    public MainWindow(DesktopServices services)
    {
        _services = services;
        Title = "NexSync";
        Width = 560;
        Height = 520;
        Content = new ContentControl();
        _services.State.Changed += Render;
        Render();
        Closed += (_, _) => _services.Dispose();
    }

    public void SignalSyncNow() => _services.State.RequestSync();

    private void Render()
    {
        WpfUserControl view = _services.State.CurrentScreen switch
        {
            AppScreen.Login => new LoginView(_services.State, _services.Auth),
            AppScreen.Setup => new SetupView(_services.State, _services.ForLocalRoot),
            AppScreen.Status => new StatusView(_services.State, _services.Auth),
            _ => new LoginView(_services.State, _services.Auth)
        };
        ((ContentControl)Content).Content = view;
    }
}
