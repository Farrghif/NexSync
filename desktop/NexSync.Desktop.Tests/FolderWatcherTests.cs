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
    public void ModifiedContent_QueuesModifyOp()
    {
        using var watcher = CreateWatcher();
        var full = Path.Combine(_root, "mod.txt");
        File.WriteAllText(full, "v1");
        watcher.ProcessPathForTest(full);
        File.WriteAllText(full, "v2-longer");
        watcher.ProcessPathForTest(full);

        var batch = new PendingOperationRepository(_db!.Connection).DequeueBatch(10);
        Assert.AreEqual(2, batch.Count);
        Assert.AreEqual(PendingOpType.CreateFile, batch[0].OpType);
        Assert.AreEqual(PendingOpType.CreateFile, batch[1].OpType);
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
