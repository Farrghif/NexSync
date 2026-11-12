using System;

namespace NexSync.Desktop;

public enum AppScreen
{
    Login,
    Setup,
    Status
}

public sealed class AppState
{
    public AppScreen CurrentScreen { get; private set; } = AppScreen.Login;
    public string? LocalRoot { get; private set; }
    public bool SyncRequested { get; private set; }
    public bool IsDarkMode { get; private set; } = false;

    public event Action? Changed;

    public void SetDarkMode(bool value)
    {
        if (IsDarkMode == value) return;
        IsDarkMode = value;
        Changed?.Invoke();
    }

    public void GoTo(AppScreen screen)
    {
        CurrentScreen = screen;
        Changed?.Invoke();
    }

    public void SetLocalRoot(string? path)
    {
        LocalRoot = path;
        Changed?.Invoke();
    }

    public void RequestSync()
    {
        SyncRequested = true;
        Changed?.Invoke();
    }

    public void ClearSyncRequest()
    {
        SyncRequested = false;
    }
}
