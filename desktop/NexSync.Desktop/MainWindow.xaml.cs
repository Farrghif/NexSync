using System.Windows;
using System.Windows.Controls;
using WpfUserControl = System.Windows.Controls.UserControl;
using NexSync.Desktop.Views;

namespace NexSync.Desktop;

public partial class MainWindow : System.Windows.Window
{
    private readonly AppState _state = new();
    private readonly ContentControl _host = new();

    public MainWindow()
    {
        Title = "NexSync";
        Width = 560;
        Height = 520;
        Content = _host;
        _state.Changed += Render;
        Render();
    }

    public void SignalSyncNow() => _state.RequestSync();

    private void Render()
    {
        WpfUserControl view = _state.CurrentScreen switch
        {
            AppScreen.Login => new LoginView(_state),
            AppScreen.Setup => new SetupView(_state),
            AppScreen.Status => new StatusView(_state),
            _ => new LoginView(_state)
        };
        _host.Content = view;
    }
}
