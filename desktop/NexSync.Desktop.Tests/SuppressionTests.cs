using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NexSync.Desktop.Data;
using NexSync.Desktop.Sync;

namespace NexSync.Desktop.Tests;

[TestClass]
public sealed class SuppressionTests
{
    private string _root = null!;
    private string _dbPath = null!;
    private LocalDb? _db;

    [TestInitialize]
    public void Setup()
    {
        _root = Path.Combine(Path.GetTempPath(), "nexsync-suppress-test", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
        _dbPath = Path.Combine(_root, ".nexsync", "local.db");
        _db = new LocalDb(_dbPath);
    }

    [TestCleanup]
    public void Teardown()
    {
        _db?.Dispose();
        _db = null;
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }

    private static string Sha256Of(byte[] bytes)
    {
        using var sha = System.Security.Cryptography.SHA256.Create();
        return Convert.ToHexString(sha.ComputeHash(bytes));
    }

    [TestMethod]
    public void PullWriteEcho_IsSuppressed_NoOpEnqueued()
    {
        var tracker = new SuppressionTracker();
        using var watcher = new FolderWatcherService(_root, _db!, tracker);
        var content = System.Text.Encoding.UTF8.GetBytes("remote-bytes");
        var hash = Sha256Of(content);
        var full = Path.Combine(_root, "pulled.txt");
        using (tracker.Suppress("pulled.txt", hash))
        {
            File.WriteAllText(full, "remote-bytes");
            watcher.ProcessPathForTest(full);
        }
        watcher.ProcessPathForTest(full);

        Assert.AreEqual(0, new PendingOperationRepository(_db!.Connection).CountPending());
    }

    [TestMethod]
    public void UserEditAfterPull_WithDifferentHash_IsEnqueued()
    {
        var tracker = new SuppressionTracker();
        using var watcher = new FolderWatcherService(_root, _db!, tracker);
        File.WriteAllText(Path.Combine(_root, "doc.txt"), "remote");
        var remoteHash = Sha256Of(System.Text.Encoding.UTF8.GetBytes("remote"));
        using (tracker.Suppress("doc.txt", remoteHash))
        {
            watcher.ProcessPathForTest(Path.Combine(_root, "doc.txt"));
        }
        Assert.AreEqual(0, new PendingOperationRepository(_db!.Connection).CountPending());

        File.WriteAllText(Path.Combine(_root, "doc.txt"), "user-edited-content");
        watcher.ProcessPathForTest(Path.Combine(_root, "doc.txt"));

        var pending = new PendingOperationRepository(_db!.Connection);
        Assert.AreEqual(1, pending.CountPending());
        Assert.AreEqual(PendingOpType.CreateFile, pending.DequeueBatch(10)[0].OpType);
    }

    [TestMethod]
    public void UserDeleteAfterPullWrite_IsEnqueued_DespiteSuppression()
    {
        var tracker = new SuppressionTracker();
        using var watcher = new FolderWatcherService(_root, _db!, tracker);
        var full = Path.Combine(_root, "temp.txt");
        File.WriteAllText(full, "remote");
        watcher.ProcessPathForTest(full);
        var pending = new PendingOperationRepository(_db!.Connection);
        Assert.AreEqual(1, pending.CountPending());
        foreach (var op in pending.DequeueBatch(10))
            pending.MarkDone(op.OperationId);

        File.Delete(full);
        watcher.ProcessPathForTest(full);

        Assert.AreEqual(1, pending.CountPending());
        Assert.AreEqual(PendingOpType.DeleteFile, pending.DequeueBatch(10)[0].OpType);
    }

    [TestMethod]
    public void InFlightWrite_PartialContent_IsSuppressedByScope()
    {
        var tracker = new SuppressionTracker();
        using var watcher = new FolderWatcherService(_root, _db!, tracker);
        var full = Path.Combine(_root, "slow.bin");
        using (tracker.Suppress("slow.bin", "FINAL-HASH-PLACEHOLDER"))
        {
            File.WriteAllBytes(full, new byte[] { 1, 2, 3 });
            watcher.ProcessPathForTest(full);
        }
        Assert.AreEqual(0, new PendingOperationRepository(_db!.Connection).CountPending());
    }

    [TestMethod]
    public void SuppressionExpires_SameContentFallsBackToHashGate()
    {
        var tracker = new SuppressionTracker(TimeSpan.FromMilliseconds(150));
        using var watcher = new FolderWatcherService(_root, _db!, tracker);
        var full = Path.Combine(_root, "exp.txt");
        File.WriteAllText(full, "v1");
        var hash = Sha256Of(System.Text.Encoding.UTF8.GetBytes("v1"));
        new LocalFileRepository(_db!.Connection).Upsert(
            new LocalFileRow("exp.txt", Guid.NewGuid(), null, hash, 2, 0, DateTime.UtcNow));
        using (tracker.Suppress("exp.txt", hash))
        {
            watcher.ProcessPathForTest(full);
        }
        Thread.Sleep(400);
        watcher.ProcessPathForTest(full);

        Assert.AreEqual(0, new PendingOperationRepository(_db!.Connection).CountPending());
        Assert.IsFalse(tracker.IsSuppressed("exp.txt", hash));
    }

    [TestMethod]
    public void ExpiredSuppression_DifferentContent_IsEnqueued()
    {
        var tracker = new SuppressionTracker(TimeSpan.FromMilliseconds(150));
        using var watcher = new FolderWatcherService(_root, _db!, tracker);
        var full = Path.Combine(_root, "exp2.txt");
        File.WriteAllText(full, "v1");
        var hash = Sha256Of(System.Text.Encoding.UTF8.GetBytes("v1"));
        new LocalFileRepository(_db!.Connection).Upsert(
            new LocalFileRow("exp2.txt", Guid.NewGuid(), null, hash, 2, 0, DateTime.UtcNow));
        using (tracker.Suppress("exp2.txt", hash))
        {
            watcher.ProcessPathForTest(full);
        }
        Assert.AreEqual(0, new PendingOperationRepository(_db!.Connection).CountPending());
        Thread.Sleep(400);
        File.WriteAllText(full, "v2-user");
        watcher.ProcessPathForTest(full);

        var pending = new PendingOperationRepository(_db!.Connection);
        Assert.AreEqual(1, pending.CountPending());
        Assert.AreEqual(PendingOpType.ModifyFile, pending.DequeueBatch(10)[0].OpType);
    }

    [TestMethod]
    public void RenameEcho_WithSuppressedPaths_ProducesNoOp()
    {
        var tracker = new SuppressionTracker();
        using var watcher = new FolderWatcherService(_root, _db!, tracker);
        var oldFull = Path.Combine(_root, "a.txt");
        var newFull = Path.Combine(_root, "b.txt");
        File.WriteAllText(oldFull, "data");
        watcher.ProcessPathForTest(oldFull);
        Assert.AreEqual(1, new PendingOperationRepository(_db!.Connection).CountPending());

        File.Move(oldFull, newFull);
        using (tracker.Suppress("a.txt", null))
        using (tracker.Suppress("b.txt", null))
        {
            watcher.ProcessPathForTest(newFull, oldFull);
        }

        Assert.AreEqual(1, new PendingOperationRepository(_db!.Connection).CountPending());
    }

    [TestMethod]
    public void GenuineUserRename_WithNoSuppression_IsEnqueued()
    {
        var tracker = new SuppressionTracker();
        using var watcher = new FolderWatcherService(_root, _db!, tracker);
        var oldFull = Path.Combine(_root, "u1.txt");
        var newFull = Path.Combine(_root, "u2.txt");
        File.WriteAllText(oldFull, "data");
        watcher.ProcessPathForTest(oldFull);
        File.Move(oldFull, newFull);
        watcher.ProcessPathForTest(newFull, oldFull);

        var batch = new PendingOperationRepository(_db!.Connection).DequeueBatch(10);
        Assert.AreEqual(2, batch.Count);
        Assert.AreEqual(PendingOpType.MoveFile, batch[1].OpType);
    }

    [TestMethod]
    public void NullTracker_WatcherBehavesAsBefore_PullEchoEnqueued()
    {
        using var watcher = new FolderWatcherService(_root, _db!);
        var full = Path.Combine(_root, "echo.txt");
        File.WriteAllText(full, "pulled");
        watcher.ProcessPathForTest(full);

        Assert.AreEqual(1, new PendingOperationRepository(_db!.Connection).CountPending());
    }

    [TestMethod]
    public void ConcurrentSuppressAndCheck_IsConsistent()
    {
        var tracker = new SuppressionTracker(TimeSpan.FromMilliseconds(100));
        var errors = new System.Collections.Concurrent.ConcurrentBag<Exception>();
        var tasks = Enumerable.Range(0, 8).Select(i => Task.Run(() =>
        {
            try
            {
                for (var n = 0; n < 200; n++)
                {
                    using (tracker.Suppress($"f{i}.txt", "HASH"))
                    {
                        tracker.IsSuppressed($"f{i}.txt", "HASH");
                        tracker.IsSuppressed($"f{i}.txt", "OTHER");
                    }
                }
            }
            catch (Exception ex)
            {
                errors.Add(ex);
            }
        })).ToArray();
        Task.WaitAll(tasks);
        Assert.AreEqual(0, errors.Count);
        Thread.Sleep(400);
        Assert.IsFalse(tracker.IsSuppressed("f0.txt", "HASH"));
    }

    [TestMethod]
    public void ScopeDispose_ReleasesScope_ButTtlTailRemains()
    {
        var tracker = new SuppressionTracker(TimeSpan.FromMinutes(5));
        var scope = tracker.Suppress("tail.txt", "H");
        Assert.IsTrue(tracker.IsSuppressed("tail.txt", "H"));
        scope.Dispose();
        Assert.IsTrue(tracker.IsSuppressed("tail.txt", "H"));
    }

    [TestMethod]
    public void Sweep_RemovesExpiredEntries_WithoutExplicitQuery()
    {
        var tracker = new SuppressionTracker(TimeSpan.FromMilliseconds(50));
        for (var i = 0; i < 70; i++)
        {
            using (tracker.Suppress($"sweep{i}.txt", "H"))
            {
            }
        }
        Thread.Sleep(200);
        for (var i = 0; i < 70; i++)
            Assert.IsFalse(tracker.IsSuppressed($"other{i}.txt", "H"));
        Assert.IsFalse(tracker.IsSuppressed("sweep0.txt", "H"));
    }

    [TestMethod]
    public void DifferentPath_NeverSuppressed()
    {
        var tracker = new SuppressionTracker();
        using (tracker.Suppress("a.txt", "H"))
        {
            Assert.IsFalse(tracker.IsSuppressed("b.txt", "H"));
            Assert.IsFalse(tracker.IsSuppressed("sub/a.txt", "H"));
        }
    }
}
