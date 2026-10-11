using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NexSync.Desktop.Data;
using NexSync.Desktop.Sync;

namespace NexSync.Desktop.Tests;

[TestClass]
public sealed class FolderWatcherTests
{
    private string _root = null!;
    private string _dbPath = null!;
    private LocalDb? _db;

    [TestInitialize]
    public void Setup()
    {
        _root = Path.Combine(Path.GetTempPath(), "nexsync-watcher-test", Guid.NewGuid().ToString("N"));
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

    private FolderWatcherService CreateWatcher() => new(_root, _db!);

    [TestMethod]
    public void NewFile_IsDetectedHashedAndQueued()
    {
        using var watcher = CreateWatcher();
        var full = Path.Combine(_root, "hello.txt");
        File.WriteAllText(full, "hello nexus");
        watcher.ProcessPathForTest(full);

        var pending = new PendingOperationRepository(_db!.Connection);
        Assert.AreEqual(1, pending.CountPending());
        var batch = pending.DequeueBatch(10);
        Assert.AreEqual(PendingOpType.CreateFile, batch[0].OpType);
        Assert.AreEqual("hello.txt", batch[0].RelPath);

        var local = new LocalFileRepository(_db.Connection).GetByPath("hello.txt");
        Assert.IsNotNull(local);
        Assert.IsNotNull(local.Hash);
        Assert.AreEqual(11L, local.Size);
    }

    [TestMethod]
    public void IdenticalContent_ProducesNoOp()
    {
        using var watcher = CreateWatcher();
        var full = Path.Combine(_root, "same.txt");
        File.WriteAllText(full, "unchanged");
        watcher.ProcessPathForTest(full);
        watcher.ProcessPathForTest(full);
        watcher.ProcessPathForTest(full);

        var pending = new PendingOperationRepository(_db!.Connection);
        Assert.AreEqual(1, pending.CountPending());
    }

    [TestMethod]
    public void ModifiedContent_QueuesModifyOpWithUpdatedHash()
    {
        using var watcher = CreateWatcher();
        var full = Path.Combine(_root, "mod.txt");
        File.WriteAllText(full, "v1");
        watcher.ProcessPathForTest(full);
        var hashBefore = new LocalFileRepository(_db!.Connection).GetByPath("mod.txt")!.Hash;
        File.WriteAllText(full, "v2-longer");
        watcher.ProcessPathForTest(full);

        var batch = new PendingOperationRepository(_db.Connection).DequeueBatch(10);
        Assert.AreEqual(2, batch.Count);
        Assert.AreEqual(PendingOpType.CreateFile, batch[0].OpType);
        Assert.AreEqual(PendingOpType.ModifyFile, batch[1].OpType);
        var hashAfter = new LocalFileRepository(_db.Connection).GetByPath("mod.txt")!.Hash;
        Assert.AreNotEqual(hashBefore, hashAfter);
    }

    [TestMethod]
    public void RenamedFile_EnqueuesSingleMoveAndTransfersRow()
    {
        using var watcher = CreateWatcher();
        var oldFull = Path.Combine(_root, "old.txt");
        var newFull = Path.Combine(_root, "new.txt");
        File.WriteAllText(oldFull, "content");
        watcher.ProcessPathForTest(oldFull);
        File.Move(oldFull, newFull);
        watcher.ProcessPathForTest(newFull, oldFull);

        var batch = new PendingOperationRepository(_db!.Connection).DequeueBatch(10);
        Assert.AreEqual(2, batch.Count);
        Assert.AreEqual(PendingOpType.CreateFile, batch[0].OpType);
        Assert.AreEqual(PendingOpType.MoveFile, batch[1].OpType);
        Assert.AreEqual("old.txt", batch[1].RelPath);
        Assert.AreEqual("new.txt", batch[1].NewRelPath);
        Assert.IsNull(new LocalFileRepository(_db.Connection).GetByPath("old.txt"));
        Assert.IsNotNull(new LocalFileRepository(_db.Connection).GetByPath("new.txt"));
    }

    [TestMethod]
    public void RenamedFolder_EnqueuesSingleMoveAndTransfersRow()
    {
        using var watcher = CreateWatcher();
        var oldDir = Path.Combine(_root, "OldDir");
        var newDir = Path.Combine(_root, "NewDir");
        Directory.CreateDirectory(oldDir);
        watcher.ProcessPathForTest(oldDir);
        Directory.Move(oldDir, newDir);
        watcher.ProcessPathForTest(newDir, oldDir);

        var batch = new PendingOperationRepository(_db!.Connection).DequeueBatch(10);
        Assert.AreEqual(2, batch.Count);
        Assert.AreEqual(PendingOpType.CreateFolder, batch[0].OpType);
        Assert.AreEqual(PendingOpType.MoveFolder, batch[1].OpType);
        Assert.IsNull(new LocalFolderRepository(_db.Connection).GetByPath("OldDir"));
        Assert.IsNotNull(new LocalFolderRepository(_db.Connection).GetByPath("NewDir"));
    }

    [TestMethod]
    public void DeletedFolder_QueuesDeleteAndDropsLocalRow()
    {
        using var watcher = CreateWatcher();
        var dir = Path.Combine(_root, "Temp");
        Directory.CreateDirectory(dir);
        watcher.ProcessPathForTest(dir);
        Directory.Delete(dir);
        watcher.ProcessPathForTest(dir);

        var batch = new PendingOperationRepository(_db!.Connection).DequeueBatch(10);
        Assert.AreEqual(2, batch.Count);
        Assert.AreEqual(PendingOpType.DeleteFolder, batch[1].OpType);
        Assert.IsNull(new LocalFolderRepository(_db.Connection).GetByPath("Temp"));
    }

    [TestMethod]
    public void OutsideRootPath_ProducesNoOp()
    {
        using var watcher = CreateWatcher();
        var outside = Path.Combine(Path.GetTempPath(), "nexsync-outside-" + Guid.NewGuid().ToString("N") + ".txt");
        try
        {
            File.WriteAllText(outside, "x");
            Assert.IsTrue(FolderWatcherService.IsInternalPath(_root, outside));
            watcher.ProcessPathForTest(outside);
            Assert.AreEqual(0, new PendingOperationRepository(_db!.Connection).CountPending());
        }
        finally
        {
            File.Delete(outside);
        }
    }

    [TestMethod]
    public void SimulatedError_SetsRecoveryFlagAndFiresEvent()
    {
        using var watcher = CreateWatcher();
        var fired = 0;
        watcher.RecoveryRequested += () => Interlocked.Increment(ref fired);
        watcher.SimulateErrorForTest();
        Assert.IsTrue(watcher.RecoveryRequired);
        Assert.AreEqual(1, fired);
    }

    [TestMethod]
    public void LockedFile_WaitsForReleaseThenQueuesCorrectHash()
    {
        using var watcher = CreateWatcher();
        var full = Path.Combine(_root, "locked.bin");
        var payload = new byte[8192];
        new Random(42).NextBytes(payload);
        using var release = new ManualResetEventSlim(false);
        using var entered = new ManualResetEventSlim(false);
        var writer = new Thread(() =>
        {
            using var fs = new FileStream(full, FileMode.Create, FileAccess.Write, FileShare.None);
            fs.Write(payload, 0, payload.Length);
            fs.Flush();
            entered.Set();
            Assert.IsTrue(release.Wait(TimeSpan.FromSeconds(15)));
        });
        writer.Start();
        Assert.IsTrue(entered.Wait(TimeSpan.FromSeconds(5)));

        Exception? workerError = null;
        var worker = new Thread(() =>
        {
            try
            {
                watcher.ProcessPathForTest(full);
            }
            catch (Exception ex)
            {
                workerError = ex;
            }
        });
        worker.Start();
        Thread.Sleep(1000);
        Assert.IsTrue(worker.IsAlive, "ProcessPath must wait for the lock instead of skipping or failing.");
        release.Set();
        Assert.IsTrue(worker.Join(TimeSpan.FromSeconds(15)));
        writer.Join(TimeSpan.FromSeconds(10));
        Assert.IsNull(workerError);

        var local = new LocalFileRepository(_db!.Connection).GetByPath("locked.bin");
        Assert.IsNotNull(local);
        Assert.AreEqual(payload.Length, local.Size);
        using var sha = System.Security.Cryptography.SHA256.Create();
        Assert.AreEqual(Convert.ToHexString(sha.ComputeHash(payload)), local.Hash);
        Assert.AreEqual(1, new PendingOperationRepository(_db.Connection).CountPending());
    }

    [TestMethod]
    public void DeletedFile_QueuesDeleteAndDropsLocalRow()
    {
        using var watcher = CreateWatcher();
        var full = Path.Combine(_root, "gone.txt");
        File.WriteAllText(full, "bye");
        watcher.ProcessPathForTest(full);
        File.Delete(full);
        watcher.ProcessPathForTest(full);

        var pending = new PendingOperationRepository(_db!.Connection);
        Assert.AreEqual(2, pending.CountPending());
        var batch = pending.DequeueBatch(10);
        Assert.AreEqual(PendingOpType.DeleteFile, batch[1].OpType);
        Assert.IsNull(new LocalFileRepository(_db.Connection).GetByPath("gone.txt"));
    }

    [TestMethod]
    public void InternalPaths_AreIgnored()
    {
        using var watcher = CreateWatcher();
        Directory.CreateDirectory(Path.Combine(_root, ".nexsync"));
        File.WriteAllText(Path.Combine(_root, ".nexsync", "local.db-journal"), "x");
        watcher.ProcessPathForTest(Path.Combine(_root, ".nexsync", "local.db-journal"));
        File.WriteAllText(Path.Combine(_root, "work.nexsync-temp"), "x");
        watcher.ProcessPathForTest(Path.Combine(_root, "work.nexsync-temp"));

        Assert.IsTrue(FolderWatcherService.IsInternalPath(_root, Path.Combine(_root, ".nexsync", "local.db")));
        Assert.IsTrue(FolderWatcherService.IsInternalPath(_root, Path.Combine(_root, "work.nexsync-temp")));
        Assert.IsFalse(FolderWatcherService.IsInternalPath(_root, Path.Combine(_root, "Projects", "a.txt")));
        Assert.AreEqual(0, new PendingOperationRepository(_db!.Connection).CountPending());
    }

    [TestMethod]
    public void NewFolder_IsQueuedAsCreateFolder()
    {
        using var watcher = CreateWatcher();
        var dir = Path.Combine(_root, "Projects");
        Directory.CreateDirectory(dir);
        watcher.ProcessPathForTest(dir);

        var pending = new PendingOperationRepository(_db!.Connection);
        Assert.AreEqual(1, pending.CountPending());
        var batch = pending.DequeueBatch(10);
        Assert.AreEqual(PendingOpType.CreateFolder, batch[0].OpType);
        Assert.AreEqual("Projects", batch[0].RelPath);
        Assert.IsNotNull(new LocalFolderRepository(_db.Connection).GetByPath("Projects"));
    }

    [TestMethod]
    public void BurstWrites_CoalesceThroughDebounceTimer()
    {
        using var watcher = CreateWatcher();
        watcher.Start();
        try
        {
            var full = Path.Combine(_root, "burst.txt");
            for (var i = 0; i < 10; i++)
                File.WriteAllText(full, "burst-" + i);
            Thread.Sleep(2500);
        }
        finally
        {
            watcher.Stop();
        }
        var count = new PendingOperationRepository(_db!.Connection).CountPending();
        Assert.IsTrue(count >= 1, "Burst must produce at least one op.");
        Assert.IsTrue(count <= 3, $"Burst of 10 rapid writes should coalesce, got {count} ops.");
    }

    [TestMethod]
    public void SlowGrowingFile_WaitsForStabilityBeforeHashing()
    {
        using var watcher = CreateWatcher();
        var full = Path.Combine(_root, "growing.bin");
        using (var fs = new FileStream(full, FileMode.Create, FileAccess.Write, FileShare.ReadWrite))
        {
            var chunk = Encoding.UTF8.GetBytes(new string('x', 4096));
            for (var i = 0; i < 5; i++)
            {
                fs.Write(chunk, 0, chunk.Length);
                fs.Flush();
                Thread.Sleep(120);
            }
        }
        watcher.ProcessPathForTest(full);

        var local = new LocalFileRepository(_db!.Connection).GetByPath("growing.bin");
        Assert.IsNotNull(local);
        Assert.AreEqual(5 * 4096L, local.Size);
    }

    [TestMethod]
    public void RecoveryFlag_StartsFalse()
    {
        using var watcher = CreateWatcher();
        Assert.IsFalse(watcher.RecoveryRequired);
    }

    [TestMethod]
    public void Watcher_StartStop_DoesNotThrow()
    {
        using var watcher = CreateWatcher();
        watcher.Start();
        watcher.Stop();
        watcher.Start();
        watcher.Stop();
    }

    [TestMethod]
    public void UnknownDeletedPath_WithNoLocalRow_QueuesNothing()
    {
        using var watcher = CreateWatcher();
        watcher.ProcessPathForTest(Path.Combine(_root, "never-seen.txt"));
        Assert.AreEqual(0, new PendingOperationRepository(_db!.Connection).CountPending());
    }
}
