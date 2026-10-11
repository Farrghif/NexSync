using System;
using System.IO;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NexSync.Desktop.Data;
using NexSync.Desktop.Sync;

namespace NexSync.Desktop.Tests;

[TestClass]
public sealed class PendingQueueTests
{
    private string _dir = null!;
    private string DbPath => Path.Combine(_dir, ".nexsync", "local.db");
    private LocalDb _db = null!;
    private PendingQueueService _queue = null!;

    [TestInitialize]
    public void Setup()
    {
        _dir = Path.Combine(Path.GetTempPath(), "nexsync-queue-test", Guid.NewGuid().ToString("N"));
        _db = new LocalDb(DbPath);
        _queue = new PendingQueueService(_db);
    }

    [TestCleanup]
    public void Teardown()
    {
        _db.Dispose();
        if (Directory.Exists(_dir))
            Directory.Delete(_dir, recursive: true);
    }

    private void SeedFolder(string relPath, Guid? serverId)
    {
        var folders = new LocalFolderRepository(_db.Connection);
        var parent = relPath.Contains('/')
            ? relPath[..relPath.LastIndexOf('/')]
            : null;
        folders.Upsert(new LocalFolderRow(relPath, serverId, parent, 0, DateTime.UtcNow));
    }

    private void SeedFile(string relPath, Guid? serverId)
    {
        new LocalFileRepository(_db.Connection).Upsert(new LocalFileRow(
            relPath, serverId, null, "hash", 10, 0, DateTime.UtcNow));
    }

    [TestMethod]
    public void Queue_SurvivesCloseAndReopen()
    {
        var id = _queue.Enqueue(PendingOpType.CreateFolder, "Projects");
        _queue.Enqueue(PendingOpType.CreateFile, "Projects/a.txt");
        _db.Dispose();

        using var reopened = new LocalDb(DbPath);
        var queue = new PendingQueueService(reopened);
        Assert.AreEqual(2, queue.CountPending());
        var batch = queue.DequeueEligibleBatch(10);
        Assert.AreEqual(2, batch.Count);
        Assert.IsTrue(batch.Any(o => o.OperationId == id));
        Assert.AreEqual(PendingOpStatus.Pending, batch[0].Status);
        Assert.AreEqual(0, batch[0].RetryCount);
    }

    [TestMethod]
    public void ScrambledArrival_StillDequeuesParentBeforeChild()
    {
        var childFirst = _queue.Enqueue(PendingOpType.CreateFile, "Projects/Form4x/home.dart");
        var parentLater = _queue.Enqueue(PendingOpType.CreateFolder, "Projects/Form4x");
        Assert.AreNotEqual(childFirst, parentLater);

        var batch = _queue.DequeueEligibleBatch(10);
        Assert.AreEqual(2, batch.Count);
        Assert.AreEqual("Projects/Form4x", batch[0].RelPath);
        Assert.AreEqual(PendingOpType.CreateFolder, batch[0].OpType);
        Assert.AreEqual("Projects/Form4x/home.dart", batch[1].RelPath);
    }

    [TestMethod]
    public void RawTimestampOrder_WouldViolateDependency_ServiceCorrectsIt()
    {
        var t0 = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var raw = new PendingOperationRepository(_db.Connection);
        using (var tx = _db.BeginTransaction())
        {
            raw.Enqueue(new PendingOperationRow(Guid.NewGuid(), PendingOpType.CreateFile,
                "Projects/child.txt", null, null, null, 0, PendingOpStatus.Pending, t0), tx);
            raw.Enqueue(new PendingOperationRow(Guid.NewGuid(), PendingOpType.CreateFolder,
                "Projects", null, null, null, 0, PendingOpStatus.Pending, t0.AddSeconds(1)), tx);
            tx.Commit();
        }
        var naive = raw.DequeueBatch(10);
        Assert.AreEqual("Projects/child.txt", naive[0].RelPath,
            "Precondition: naive (CreatedAt, OperationId) order puts the child first.");

        var batch = _queue.DequeueEligibleBatch(10);
        Assert.AreEqual("Projects", batch[0].RelPath);
        Assert.AreEqual("Projects/child.txt", batch[1].RelPath);
    }

    [TestMethod]
    public void ChildBlocked_WhenParentHasNoServerIdAndNoPendingCreate()
    {
        SeedFolder("Projects", serverId: null);
        _queue.Enqueue(PendingOpType.CreateFile, "Projects/orphan.txt");
        _queue.Enqueue(PendingOpType.CreateFolder, "Unrelated");

        var batch = _queue.DequeueEligibleBatch(10);
        Assert.AreEqual(1, batch.Count);
        Assert.AreEqual("Unrelated", batch[0].RelPath);
    }

    [TestMethod]
    public void ChildBecomesEligible_AfterParentGainsServerId()
    {
        SeedFolder("Projects", serverId: null);
        _queue.Enqueue(PendingOpType.CreateFile, "Projects/waiting.txt");
        Assert.AreEqual(0, _queue.DequeueEligibleBatch(10).Count);

        SeedFolder("Projects", serverId: Guid.NewGuid());
        var batch = _queue.DequeueEligibleBatch(10);
        Assert.AreEqual(1, batch.Count);
        Assert.AreEqual("Projects/waiting.txt", batch[0].RelPath);
    }

    [TestMethod]
    public void DeepNesting_OrdersAllLevels()
    {
        SeedFolder("A", serverId: Guid.NewGuid());
        _queue.Enqueue(PendingOpType.CreateFile, "A/B/C/deep.txt");
        _queue.Enqueue(PendingOpType.CreateFolder, "A/B/C");
        _queue.Enqueue(PendingOpType.CreateFolder, "A/B");

        var batch = _queue.DequeueEligibleBatch(10);
        Assert.AreEqual(3, batch.Count);
        Assert.AreEqual("A/B", batch[0].RelPath);
        Assert.AreEqual("A/B/C", batch[1].RelPath);
        Assert.AreEqual("A/B/C/deep.txt", batch[2].RelPath);
    }

    [TestMethod]
    public void Complete_Success_RemovesOp_Failure_BumpsRetry()
    {
        var id = _queue.Enqueue(PendingOpType.ModifyFile, "f.txt");
        SeedFile("f.txt", Guid.NewGuid());
        var batch = _queue.DequeueEligibleBatch(10);
        _queue.Complete(batch[0], success: false);
        Assert.AreEqual(0, _queue.CountPending(), "Failed ops leave the Pending set until requeued.");

        var raw = new PendingOperationRepository(_db.Connection);
        var again = raw.DequeueBatch(10);
        Assert.AreEqual(0, again.Count, "Failed ops must not be Pending until requeued.");
        _queue.RequeueFailed();
        Assert.AreEqual(1, _queue.CountPending());

        var retry = _queue.DequeueEligibleBatch(10);
        Assert.AreEqual(1, retry[0].RetryCount);
        _queue.Complete(retry[0], success: true);
        Assert.AreEqual(0, _queue.CountPending());
        Assert.AreNotEqual(Guid.Empty, id);
    }

    [TestMethod]
    public void IdenticalDuplicateEnqueue_ReturnsExistingId_NoSecondRow()
    {
        var first = _queue.Enqueue(PendingOpType.ModifyFile, "dup.txt");
        var second = _queue.Enqueue(PendingOpType.ModifyFile, "dup.txt");
        Assert.AreEqual(first, second);
        Assert.AreEqual(1, _queue.CountPending());
    }

    [TestMethod]
    public void DifferentOpTypes_SamePath_AreDistinctRows()
    {
        SeedFile("x.txt", Guid.NewGuid());
        _queue.Enqueue(PendingOpType.ModifyFile, "x.txt");
        _queue.Enqueue(PendingOpType.DeleteFile, "x.txt");
        Assert.AreEqual(2, _queue.CountPending());
    }

    [TestMethod]
    public void RenameMove_StaysSingleLogicalOp_WithBothPaths()
    {
        SeedFile("old.txt", Guid.NewGuid());
        _queue.Enqueue(PendingOpType.MoveFile, "old.txt", newRelPath: "new.txt");
        var batch = _queue.DequeueEligibleBatch(10);
        Assert.AreEqual(1, batch.Count);
        Assert.AreEqual(PendingOpType.MoveFile, batch[0].OpType);
        Assert.AreEqual("old.txt", batch[0].RelPath);
        Assert.AreEqual("new.txt", batch[0].NewRelPath);
    }

    [TestMethod]
    public void DeleteOfUnknownEntity_IsDroppable_DeleteOfKnown_IsEligible()
    {
        var ghost = new PendingOperationRow(Guid.NewGuid(), PendingOpType.DeleteFile,
            "ghost.txt", null, null, null, 0, PendingOpStatus.Pending, DateTime.UtcNow);
        using (var tx = _db.BeginTransaction())
        {
            new PendingOperationRepository(_db.Connection).Enqueue(ghost, tx);
            tx.Commit();
        }
        Assert.IsTrue(_queue.TryDropRedundantDelete(ghost));

        SeedFile("real.txt", Guid.NewGuid());
        var real = new PendingOperationRow(Guid.NewGuid(), PendingOpType.DeleteFile,
            "real.txt", null, null, null, 0, PendingOpStatus.Pending, DateTime.UtcNow);
        using (var tx = _db.BeginTransaction())
        {
            new PendingOperationRepository(_db.Connection).Enqueue(real, tx);
            tx.Commit();
        }
        Assert.IsFalse(_queue.TryDropRedundantDelete(real));
        Assert.AreEqual(1, _queue.CountPending());
    }

    [TestMethod]
    public void AllEightOpTypes_RoundTrip()
    {
        SeedFolder("K", serverId: Guid.NewGuid());
        SeedFile("K/f.txt", Guid.NewGuid());
        var types = new[]
        {
            (PendingOpType.CreateFile, "K/n1.txt", null),
            (PendingOpType.ModifyFile, "K/f.txt", null),
            (PendingOpType.DeleteFile, "K/f.txt", null),
            (PendingOpType.MoveFile, "K/f.txt", "K/g.txt"),
            (PendingOpType.CreateFolder, "K/sub", null),
            (PendingOpType.DeleteFolder, "K", null),
            (PendingOpType.MoveFolder, "K", "K2"),
            (PendingOpType.RenameFolder, "K", "K3"),
        };
        foreach (var (type, path, dest) in types)
            _queue.Enqueue(type, path, dest);
        var batch = _queue.DequeueEligibleBatch(100);
        Assert.AreEqual(8, batch.Count);
        foreach (var (type, _, _) in types)
            Assert.IsTrue(batch.Any(o => o.OpType == type), $"Missing {type}");
    }

    [TestMethod]
    public void BatchBound_RespectedWithEligibility()
    {
        SeedFolder("P", serverId: Guid.NewGuid());
        _queue.Enqueue(PendingOpType.CreateFile, "P/1.txt");
        _queue.Enqueue(PendingOpType.CreateFile, "P/2.txt");
        _queue.Enqueue(PendingOpType.CreateFile, "P/3.txt");
        Assert.AreEqual(2, _queue.DequeueEligibleBatch(2).Count);
        Assert.AreEqual(3, _queue.CountPending());
    }

    [TestMethod]
    public void FailedTransaction_LeavesQueueConsistent()
    {
        _queue.Enqueue(PendingOpType.CreateFolder, "Stable");
        using (var tx = _db.BeginTransaction())
        {
            new PendingOperationRepository(_db.Connection).Enqueue(
                new PendingOperationRow(Guid.NewGuid(), PendingOpType.CreateFolder,
                    "Aborted", null, null, null, 0, PendingOpStatus.Pending, DateTime.UtcNow), tx);
        }
        var batch = _queue.DequeueEligibleBatch(10);
        Assert.AreEqual(1, batch.Count);
        Assert.AreEqual("Stable", batch[0].RelPath);
    }
}
