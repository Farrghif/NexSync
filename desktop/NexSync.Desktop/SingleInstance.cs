using System;
using System.Threading;

namespace NexSync.Desktop;

public sealed class SingleInstance(string? mutexName = null) : IDisposable
{
    public const string MutexName = @"Global\NexSync-Desktop";

    private readonly string _mutexName = mutexName ?? MutexName;
    private Mutex? _mutex;

    public bool TryAcquire()
    {
        _mutex = new Mutex(initiallyOwned: true, _mutexName, out var createdNew);
        if (!createdNew)
        {
            _mutex.Dispose();
            _mutex = null;
            return false;
        }
        return true;
    }

    public void Dispose()
    {
        if (_mutex is not null)
        {
            try { _mutex.ReleaseMutex(); } catch (ApplicationException) { }
            _mutex.Dispose();
            _mutex = null;
        }
    }
}
