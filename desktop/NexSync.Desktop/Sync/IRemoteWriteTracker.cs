using System;

namespace NexSync.Desktop.Sync;

public interface IRemoteWriteTracker
{
    IDisposable Suppress(string relPath, string? expectedHash);
    bool IsSuppressed(string relPath, string? currentHash);
}

public sealed class NullRemoteWriteTracker : IRemoteWriteTracker
{
    public static readonly NullRemoteWriteTracker Instance = new();
    public IDisposable Suppress(string relPath, string? expectedHash) => NullScope.Instance;
    public bool IsSuppressed(string relPath, string? currentHash) => false;

    private sealed class NullScope : IDisposable
    {
        public static readonly NullScope Instance = new();
        public void Dispose() { }
    }
}
