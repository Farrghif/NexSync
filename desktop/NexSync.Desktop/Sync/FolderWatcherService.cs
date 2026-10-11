using System;
using System.Collections.Concurrent;
using System.IO;
using System.Security.Cryptography;
using System.Threading;
using NexSync.Desktop.Data;

namespace NexSync.Desktop.Sync;

public sealed class FolderWatcherService : IDisposable
{
    public const string InternalDirName = ".nexsync";
    public const string TempSuffix = ".nexsync-temp";
    public static readonly TimeSpan DebounceDelay = TimeSpan.FromMilliseconds(750);
    private const int StabilitySamples = 3;
    private static readonly TimeSpan StabilityInterval = TimeSpan.FromMilliseconds(150);
    private const int MaxStabilityAttempts = 40;

    private readonly string _root;
    private readonly LocalDb _db;
    private readonly FileSystemWatcher _watcher;
    private readonly ConcurrentDictionary<string, Timer> _debounce = new(StringComparer.OrdinalIgnoreCase);
    private bool _disposed;

    public bool RecoveryRequired { get; private set; }
    public event Action? RecoveryRequested;

    public FolderWatcherService(string root, LocalDb db)
    {
        _root = Path.GetFullPath(root);
        _db = db;
        _watcher = new FileSystemWatcher(_root)
        {
            IncludeSubdirectories = true,
            NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName
                | NotifyFilters.Size | NotifyFilters.LastWrite,
            EnableRaisingEvents = false
        };
        _watcher.Created += (_, e) => Schedule(e.FullPath);
        _watcher.Changed += (_, e) => Schedule(e.FullPath);
        _watcher.Deleted += (_, e) => Schedule(e.FullPath);
        _watcher.Renamed += (_, e) =>
        {
            Cancel(e.OldFullPath);
            Schedule(e.FullPath, oldFullPath: e.OldFullPath);
        };
        _watcher.Error += (_, _) =>
        {
            RecoveryRequired = true;
            RecoveryRequested?.Invoke();
        };
    }

    public void Start() => _watcher.EnableRaisingEvents = true;

    public void Stop() => _watcher.EnableRaisingEvents = false;

    public static bool IsInternalPath(string root, string fullPath)
    {
        var rel = Path.GetRelativePath(root, fullPath);
        if (rel.StartsWith("..", StringComparison.Ordinal))
            return true;
        if (rel.Equals(InternalDirName, StringComparison.OrdinalIgnoreCase)
            || rel.StartsWith(InternalDirName + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
            || rel.StartsWith(InternalDirName + '/', StringComparison.OrdinalIgnoreCase))
            return true;
        var name = Path.GetFileName(fullPath);
        return name.EndsWith(TempSuffix, StringComparison.OrdinalIgnoreCase);
    }

    private void Schedule(string fullPath, string? oldFullPath = null)
    {
        if (IsInternalPath(_root, fullPath))
            return;
        var payload = new PendingPath(fullPath, oldFullPath);
        _ = _debounce.AddOrUpdate(fullPath,
            _ => new Timer(ProcessPath, payload, DebounceDelay, Timeout.InfiniteTimeSpan),
            (_, existing) =>
            {
                existing.Change(DebounceDelay, Timeout.InfiniteTimeSpan);
                return existing;
            });
    }

    private sealed record PendingPath(string FullPath, string? OldFullPath);

    private void Cancel(string fullPath)
    {
        if (_debounce.TryRemove(fullPath, out var timer))
            timer.Dispose();
    }

    public void ProcessPathForTest(string fullPath, string? oldFullPath = null)
    {
        ProcessPathInternal(fullPath, oldFullPath);
    }

    public void SimulateErrorForTest()
    {
        RecoveryRequired = true;
        RecoveryRequested?.Invoke();
    }

    private void ProcessPath(object? state)
    {
        var payload = (PendingPath)state!;
        Cancel(payload.FullPath);
        ProcessPathInternal(payload.FullPath, payload.OldFullPath);
    }

    private void ProcessPathInternal(string fullPath, string? oldFullPath = null)
    {
        if (IsInternalPath(_root, fullPath))
            return;
        string relPath;
        try
        {
            relPath = LocalPath.Canonicalize(Path.GetRelativePath(_root, fullPath).Replace('\\', '/'));
        }
        catch (ArgumentException)
        {
            return;
        }

        if (oldFullPath is not null && !IsInternalPath(_root, oldFullPath))
        {
            string oldRel;
            try
            {
                oldRel = LocalPath.Canonicalize(Path.GetRelativePath(_root, oldFullPath).Replace('\\', '/'));
            }
            catch (ArgumentException)
            {
                oldRel = string.Empty;
            }
            if (!string.IsNullOrEmpty(oldRel) && !string.Equals(oldRel, relPath, StringComparison.OrdinalIgnoreCase))
            {
                EnqueueRename(oldRel, relPath, fullPath);
                return;
            }
        }

        var isDir = Directory.Exists(fullPath);
        var isFile = File.Exists(fullPath);
        if (!isDir && !isFile)
        {
            EnqueueDelete(relPath);
            return;
        }
        if (isDir)
        {
            EnqueueFolderSeen(relPath);
            return;
        }
        if (!WaitForStable(fullPath))
        {
            RecoveryRequired = true;
            RecoveryRequested?.Invoke();
            return;
        }
        EnqueueFileSeen(relPath, fullPath);
    }

    private static bool WaitForStable(string fullPath)
    {
        long? lastSize = null;
        var lastWrite = DateTime.MinValue;
        var stableCount = 0;
        for (var i = 0; i < MaxStabilityAttempts; i++)
        {
            FileInfo info;
            try
            {
                info = new FileInfo(fullPath);
                if (!info.Exists)
                    return true;
                using (info.Open(FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                {
                }
            }
            catch (IOException)
            {
                Thread.Sleep(StabilityInterval);
                continue;
            }
            if (lastSize.HasValue && info.Length == lastSize.Value && info.LastWriteTimeUtc == lastWrite)
            {
                stableCount++;
                if (stableCount >= StabilitySamples)
                    return true;
            }
            else
            {
                stableCount = 1;
                lastSize = info.Length;
                lastWrite = info.LastWriteTimeUtc;
            }
            Thread.Sleep(StabilityInterval);
        }
        return false;
    }

    private void EnqueueDelete(string relPath)
    {
        var folders = new LocalFolderRepository(_db.Connection);
        var files = new LocalFileRepository(_db.Connection);
        var pending = new PendingOperationRepository(_db.Connection);
        var folder = folders.GetByPath(relPath);
        if (folder is not null)
        {
            pending.Enqueue(new PendingOperationRow(Guid.NewGuid(), PendingOpType.DeleteFolder,
                relPath, null, folder.ServerFolderId, null, 0, PendingOpStatus.Pending, DateTime.UtcNow));
            folders.Delete(relPath);
            return;
        }
        var file = files.GetByPath(relPath);
        if (file is not null)
        {
            pending.Enqueue(new PendingOperationRow(Guid.NewGuid(), PendingOpType.DeleteFile,
                relPath, null, file.ServerFileId, null, 0, PendingOpStatus.Pending, DateTime.UtcNow));
            files.Delete(relPath);
        }
    }

    private void EnqueueFolderSeen(string relPath)
    {
        var folders = new LocalFolderRepository(_db.Connection);
        var pending = new PendingOperationRepository(_db.Connection);
        if (folders.GetByPath(relPath) is not null)
            return;
        var parent = Path.GetDirectoryName(relPath.Replace('/', Path.DirectorySeparatorChar));
        var parentCanon = string.IsNullOrEmpty(parent) ? null : LocalPath.Canonicalize(parent.Replace('\\', '/'));
        folders.Upsert(new LocalFolderRow(relPath, null, parentCanon, 0, DateTime.UtcNow));
        pending.Enqueue(new PendingOperationRow(Guid.NewGuid(), PendingOpType.CreateFolder,
            relPath, null, null, null, 0, PendingOpStatus.Pending, DateTime.UtcNow));
    }

    private void EnqueueRename(string oldRelPath, string newRelPath, string newFullPath)
    {
        var folders = new LocalFolderRepository(_db.Connection);
        var files = new LocalFileRepository(_db.Connection);
        var pending = new PendingOperationRepository(_db.Connection);
        var folder = folders.GetByPath(oldRelPath);
        if (folder is not null && Directory.Exists(newFullPath))
        {
            var parent = Path.GetDirectoryName(newRelPath.Replace('/', Path.DirectorySeparatorChar));
            var parentCanon = string.IsNullOrEmpty(parent) ? null : LocalPath.Canonicalize(parent.Replace('\\', '/'));
            folders.Delete(oldRelPath);
            folders.Upsert(new LocalFolderRow(newRelPath, folder.ServerFolderId, parentCanon, folder.LastAppliedSequence, DateTime.UtcNow));
            pending.Enqueue(new PendingOperationRow(Guid.NewGuid(), PendingOpType.MoveFolder,
                oldRelPath, newRelPath, folder.ServerFolderId, null, 0, PendingOpStatus.Pending, DateTime.UtcNow));
            return;
        }
        var file = files.GetByPath(oldRelPath);
        if (file is not null && File.Exists(newFullPath))
        {
            files.Delete(oldRelPath);
            files.Upsert(new LocalFileRow(newRelPath, file.ServerFileId, file.ServerFolderId, file.Hash, file.Size, file.LastAppliedSequence, DateTime.UtcNow));
            pending.Enqueue(new PendingOperationRow(Guid.NewGuid(), PendingOpType.MoveFile,
                oldRelPath, newRelPath, file.ServerFileId, null, 0, PendingOpStatus.Pending, DateTime.UtcNow));
            return;
        }
    }

    private void EnqueueFileSeen(string relPath, string fullPath)
    {
        string hash;
        long size;
        try
        {
            using var sha = SHA256.Create();
            using var stream = new FileStream(fullPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            size = stream.Length;
            var computed = sha.ComputeHash(stream);
            hash = Convert.ToHexString(computed);
        }
        catch (IOException)
        {
            RecoveryRequired = true;
            RecoveryRequested?.Invoke();
            return;
        }
        var files = new LocalFileRepository(_db.Connection);
        var pending = new PendingOperationRepository(_db.Connection);
        var existing = files.GetByPath(relPath);
        if (existing is not null && string.Equals(existing.Hash, hash, StringComparison.OrdinalIgnoreCase))
            return;
        var op = existing is null ? PendingOpType.CreateFile : PendingOpType.ModifyFile;
        files.Upsert(new LocalFileRow(relPath, existing?.ServerFileId, existing?.ServerFolderId, hash, size, existing?.LastAppliedSequence ?? 0, DateTime.UtcNow));
        pending.Enqueue(new PendingOperationRow(Guid.NewGuid(), op,
            relPath, null, existing?.ServerFileId, null, 0, PendingOpStatus.Pending, DateTime.UtcNow));
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        Stop();
        foreach (var timer in _debounce.Values)
            timer.Dispose();
        _debounce.Clear();
        _watcher.Dispose();
    }
}
