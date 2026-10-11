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

    public event Action? Changed;

    public void GoTo(AppScreen screen)
    {
        CurrentScreen = screen;
        Changed?.Invoke();
    }

    public void SetLocalRoot(string path)
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
