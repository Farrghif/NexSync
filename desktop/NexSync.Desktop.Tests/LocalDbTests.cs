using System;
using System.IO;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NexSync.Desktop.Data;

namespace NexSync.Desktop.Tests;

[TestClass]
public sealed class LocalPathTests
{
    [TestMethod]
    [DataRow(@"Projects\Form4x\home.dart", "Projects/Form4x/home.dart")]
    [DataRow("Projects/Form4x/home.dart", "Projects/Form4x/home.dart")]
    [DataRow(@"Projects\\\Form4x", "Projects/Form4x")]
    public void Canonicalize_NormalizesSeparators(string input, string expected)
    {
        Assert.AreEqual(expected, LocalPath.Canonicalize(input));
    }

    [TestMethod]
    [DataRow("")]
    [DataRow("   ")]
    [DataRow("Projects/./home.dart")]
    [DataRow("Projects/../home.dart")]
    [DataRow("trailingdot./x")]
    [DataRow("trailingspace /x")]
    public void Canonicalize_RejectsNonCanonical(string input)
    {
        Assert.ThrowsExactly<ArgumentException>(() => LocalPath.Canonicalize(input));
    }
}

[TestClass]
public sealed class LocalDbTests
{
    private string _dir = null!;
    private string DbPath => Path.Combine(_dir, ".nexsync", "local.db");

    [TestInitialize]
    public void Setup() => _dir = Path.Combine(Path.GetTempPath(), "nexsync-localdb-test", Guid.NewGuid().ToString("N"));

    [TestCleanup]
    public void Teardown()
    {
        if (Directory.Exists(_dir))
            Directory.Delete(_dir, recursive: true);
    }

    [TestMethod]
    public void Database_CreatedAtSpecifiedLocation_WithAllTables()
    {
        using var db = new LocalDb(DbPath);
        Assert.IsTrue(File.Exists(DbPath));
        var pending = new PendingOperationRepository(db.Connection);
        var state = new SyncStateRepository(db.Connection);
        Assert.AreEqual(0, pending.CountPending());
        Assert.IsNull(state.Get("LastPulledSequence"));
    }

    [TestMethod]
    public void State_PersistsAcrossCloseAndReopen()
    {
        using (var db = new LocalDb(DbPath))
        {
            var folders = new LocalFolderRepository(db.Connection);
            folders.Upsert(new LocalFolderRow("Projects", Guid.NewGuid(), null, 42, DateTime.UtcNow));
            new SyncStateRepository(db.Connection).Set("LastPulledSequence", "42");
        }
        using (var reopened = new LocalDb(DbPath))
        {
            var folder = new LocalFolderRepository(reopened.Connection).GetByPath("Projects");
            Assert.IsNotNull(folder);
            Assert.AreEqual(42L, folder.LastAppliedSequence);
            Assert.AreEqual("42", new SyncStateRepository(reopened.Connection).Get("LastPulledSequence"));
        }
    }

    [TestMethod]
    public void FolderCrud_ParentChildRoundTrip()
    {
        using var db = new LocalDb(DbPath);
        var repo = new LocalFolderRepository(db.Connection);
        var parentId = Guid.NewGuid();
        var childId = Guid.NewGuid();
        repo.Upsert(new LocalFolderRow("Projects", parentId, null, 1, DateTime.UtcNow));
        repo.Upsert(new LocalFolderRow("Projects/Form4x", childId, "Projects", 2, DateTime.UtcNow));

        Assert.AreEqual(childId, repo.GetByServerId(childId)!.ServerFolderId);
        var children = repo.GetChildren("Projects");
        Assert.AreEqual(1, children.Count);
        Assert.AreEqual("Projects/Form4x", children[0].RelativePath);

        repo.Rename("Projects/Form4x", "Projects/App");
        Assert.IsNull(repo.GetByPath("Projects/Form4x"));
        Assert.AreEqual(childId, repo.GetByPath("Projects/App")!.ServerFolderId);

        repo.Delete("Projects/App");
        repo.Delete("Projects");
        Assert.IsNull(repo.GetByPath("Projects"));
    }

    [TestMethod]
    public void FileCrud_MoveAndLookupByServerId()
    {
        using var db = new LocalDb(DbPath);
        var repo = new LocalFileRepository(db.Connection);
        var fileId = Guid.NewGuid();
        var folderId = Guid.NewGuid();
        repo.Upsert(new LocalFileRow("Projects/a.txt", fileId, folderId, "abc", 3, 7, DateTime.UtcNow));

        var byId = repo.GetByServerId(fileId);
        Assert.IsNotNull(byId);
        Assert.AreEqual("abc", byId.Hash);

        repo.Move("Projects/a.txt", "Projects/b.txt");
        Assert.IsNull(repo.GetByPath("Projects/a.txt"));
        Assert.AreEqual(fileId, repo.GetByPath("Projects/b.txt")!.ServerFileId);

        var inFolder = repo.GetByFolderPath("Projects");
        Assert.AreEqual(1, inFolder.Count);

        repo.Delete("Projects/b.txt");
        Assert.IsNull(repo.GetByServerId(fileId));
    }

    [TestMethod]
    public void GetByFolderPath_DoesNotMatchSiblingPrefix()
    {
        using var db = new LocalDb(DbPath);
        var repo = new LocalFileRepository(db.Connection);
        repo.Upsert(new LocalFileRow("Projects/a.txt", Guid.NewGuid(), null, "h", 1, 0, DateTime.UtcNow));
        repo.Upsert(new LocalFileRow("ProjectsBackup/b.txt", Guid.NewGuid(), null, "h", 1, 0, DateTime.UtcNow));
        var inFolder = repo.GetByFolderPath("Projects");
        Assert.AreEqual(1, inFolder.Count);
        Assert.AreEqual("Projects/a.txt", inFolder[0].RelativePath);
    }

    [TestMethod]
    public void PendingQueue_OrderedByCreatedThenId()
    {
        using var db = new LocalDb(DbPath);
        var queue = new PendingOperationRepository(db.Connection);
        var t0 = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        queue.Enqueue(new PendingOperationRow(Guid.Parse("11111111-1111-1111-1111-111111111111"), PendingOpType.CreateFile, "b.txt", null, null, null, 0, PendingOpStatus.Pending, t0.AddSeconds(2)));
        queue.Enqueue(new PendingOperationRow(Guid.Parse("22222222-2222-2222-2222-222222222222"), PendingOpType.CreateFolder, "Projects", null, null, null, 0, PendingOpStatus.Pending, t0.AddSeconds(1)));
        var batch = queue.DequeueBatch(10);
        Assert.AreEqual(2, batch.Count);
        Assert.AreEqual(PendingOpType.CreateFolder, batch[0].OpType);
        Assert.AreEqual(PendingOpType.CreateFile, batch[1].OpType);
        Assert.AreEqual(2, queue.CountPending());

        queue.MarkDone(batch[0].OperationId);
        Assert.AreEqual(1, queue.CountPending());
        queue.MarkFailed(batch[1].OperationId);
        Assert.AreEqual(0, queue.CountPending());
        queue.RequeueFailedAsPending();
        Assert.AreEqual(1, queue.CountPending());
    }

    [TestMethod]
    public void ProcessedOperations_DeduplicatesById()
    {
        using var db = new LocalDb(DbPath);
        var repo = new ProcessedOperationRepository(db.Connection);
        var id = Guid.NewGuid();
        Assert.IsFalse(repo.Contains(id));
        repo.Record(new ProcessedOperationRow(id, "201 created", DateTime.UtcNow));
        repo.Record(new ProcessedOperationRow(id, "201 created", DateTime.UtcNow));
        Assert.IsTrue(repo.Contains(id));
    }

    [TestMethod]
    public void SyncState_GetSetRoundTrip()
    {
        using var db = new LocalDb(DbPath);
        var state = new SyncStateRepository(db.Connection);
        Assert.AreEqual(0L, state.GetSequence("LastPulledSequence"));
        state.Set("LastPulledSequence", "150");
        state.Set("DeviceId", Guid.NewGuid().ToString());
        Assert.AreEqual(150L, state.GetSequence("LastPulledSequence"));
        state.Set("LastPulledSequence", "151");
        Assert.AreEqual(151L, state.GetSequence("LastPulledSequence"));
    }

    [TestMethod]
    public void MultiRecordTransaction_RollsBackOnFailure()
    {
        using var db = new LocalDb(DbPath);
        var folders = new LocalFolderRepository(db.Connection);
        using (var tx = db.BeginTransaction())
        {
            folders.Upsert(new LocalFolderRow("Tx", null, null, 0, DateTime.UtcNow), tx);
            tx.Rollback();
        }
        Assert.IsNull(folders.GetByPath("Tx"));

        using (var tx = db.BeginTransaction())
        {
            folders.Upsert(new LocalFolderRow("Tx2", null, null, 0, DateTime.UtcNow), tx);
            new SyncStateRepository(db.Connection).Set("Marker", "1", tx);
            tx.Commit();
        }
        Assert.IsNotNull(folders.GetByPath("Tx2"));
    }

    [TestMethod]
    public void DuplicateRelativePath_UpsertsInsteadOfDuplicating()
    {
        using var db = new LocalDb(DbPath);
        var repo = new LocalFileRepository(db.Connection);
        repo.Upsert(new LocalFileRow("dup.txt", Guid.NewGuid(), null, "h1", 1, 0, DateTime.UtcNow));
        var id2 = Guid.NewGuid();
        repo.Upsert(new LocalFileRow("dup.txt", id2, null, "h2", 2, 5, DateTime.UtcNow));
        var row = repo.GetByPath("dup.txt")!;
        Assert.AreEqual(id2, row.ServerFileId);
        Assert.AreEqual("h2", row.Hash);
    }
}

