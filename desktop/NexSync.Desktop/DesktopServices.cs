using System;
using System.IO;
using NexSync.Desktop.Auth;

namespace NexSync.Desktop;

public sealed class DesktopServices : IDisposable
{
    public AppState State { get; } = new();
    public DesktopAuthService Auth { get; }

    public DesktopServices(string? baseUrl = null, string? dataDirectory = null)
    {
        var url = baseUrl ?? Environment.GetEnvironmentVariable("NEXSYNC_API_URL") ?? "http://localhost:5000";
        var dir = dataDirectory ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "NexSync");
        Auth = new DesktopAuthService(url, dir);
    }

    public void Dispose() => Auth.Dispose();
}
