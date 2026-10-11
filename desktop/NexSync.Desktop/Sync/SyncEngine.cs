using System;
using System.IO;
using NexSync.Desktop.Auth;
using NexSync.Desktop.Data;

namespace NexSync.Desktop.Sync;

public sealed class SyncEngine : IDisposable
{
    public LocalDb Db { get; }
    public PendingQueueService Queue { get; }
    public SuppressionTracker Suppression { get; }
    public ConflictHandler Conflicts { get; }
    public PushPipeline Push { get; }
    public PullPipeline Pull { get; }
    public SyncCoordinator Coordinator { get; }
    public RecoveryService Recovery { get; }
    public FolderWatcherService Watcher { get; }

    public SyncEngine(LocalDb db, DesktopAuthService auth, string localRoot)
    {
        Directory.CreateDirectory(localRoot);
        Db = db;
        Queue = new PendingQueueService(db);
        Suppression = new SuppressionTracker();
        Conflicts = new ConflictHandler(db, localRoot);
        Push = new PushPipeline(db, Queue, auth, localRoot);
        Pull = new PullPipeline(db, Queue, auth, localRoot, Suppression, Conflicts);
        Coordinator = new SyncCoordinator(db, Queue, Push, conflicts: Conflicts);
        Recovery = new RecoveryService(db, Queue, auth, localRoot);
        Watcher = new FolderWatcherService(localRoot, db, Suppression);
        Watcher.RecoveryRequested += OnWatcherRecoveryRequested;
    }

    public event Action? RecoveryRequired;

    private void OnWatcherRecoveryRequested() => RecoveryRequired?.Invoke();

    public void Dispose()
    {
        Watcher.RecoveryRequested -= OnWatcherRecoveryRequested;
        Watcher.Dispose();
    }
}

public static class SyncEngineFactory
{
    public static SyncEngine Create(LocalDb db, DesktopAuthService auth, string localRoot) =>
        new(db, auth, localRoot);
}
