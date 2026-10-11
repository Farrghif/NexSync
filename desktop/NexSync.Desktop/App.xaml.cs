using System;
using System.Threading;
using System.Windows;

namespace NexSync.Desktop;

public partial class App : System.Windows.Application
{
    private SingleInstance? _instance;
    private TrayIcon? _tray;

    protected override void OnStartup(StartupEventArgs e)
    {
        _instance = new SingleInstance();
        if (!_instance.TryAcquire())
        {
            MessageBox.Show("NexSync is already running.", "NexSync",
                MessageBoxButton.OK, MessageBoxImage.Information);
            Shutdown();
            return;
        }

        _tray = new TrayIcon(
            onOpen: () => MainWindow?.Show(),
            onSyncNow: () => (MainWindow as MainWindow)?.SignalSyncNow(),
            onExit: Shutdown);

        var window = new MainWindow();
        window.Closed += (_, _) => Shutdown();
        MainWindow = window;
        window.Show();

        base.OnStartup(e);
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _tray?.Dispose();
        _instance?.Dispose();
        base.OnExit(e);
    }
}
