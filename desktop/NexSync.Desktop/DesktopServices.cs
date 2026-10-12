using System;
using System.IO;
using NexSync.Desktop.Auth;
using NexSync.Desktop.Sync;

namespace NexSync.Desktop;

public sealed class DesktopServices : IDisposable
{
    public AppState State { get; } = new();
    public DesktopAuthService Auth { get; }
    private readonly string _dataDirectory;

    public DesktopServices(string? baseUrl = null, string? dataDirectory = null)
    {
        var url = baseUrl ?? Environment.GetEnvironmentVariable("NEXSYNC_API_URL") ?? "http://localhost:5000";
        _dataDirectory = dataDirectory ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "NexSync");
        Auth = new DesktopAuthService(url, _dataDirectory);
    }

    public SetupService ForLocalRoot(string localRoot) => SetupService.ForLocalRoot(localRoot);

    public void Dispose() => Auth.Dispose();
}
