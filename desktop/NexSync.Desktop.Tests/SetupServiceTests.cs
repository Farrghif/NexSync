using System;
using System.IO;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NexSync.Desktop.Data;
using NexSync.Desktop.Sync;

namespace NexSync.Desktop.Tests;

[TestClass]
public sealed class SetupServiceTests
{
    private string _dir = null!;
    private string _root = null!;
    private string DbPath => Path.Combine(_dir, ".nexsync", "local.db");
    private LocalDb _db = null!;

    [TestInitialize]
    public void Setup()
    {
        _dir = Path.Combine(Path.GetTempPath(), "nexsync-setup-test", Guid.NewGuid().ToString("N"));
        _root = Path.Combine(_dir, "files");
        Directory.CreateDirectory(_root);
        _db = new LocalDb(DbPath);
    }

    [TestCleanup]
    public void Teardown()
    {
        _db.Dispose();
        if (Directory.Exists(_dir))
            Directory.Delete(_dir, recursive: true);
    }

    private SetupService Create(string? root = null, Func<string, Stream>? openRead = null)
        => new(_db, new PendingQueueService(_db), root ?? _root, openRead);

    private static string? Choice(LocalDb db)
        => new SyncStateRepository(db.Connection).Get(SetupService.SetupChoiceKey);

    [TestMethod]
    public void EmptyFolder_ApplyEmpty_SucceedsRecordsChoice()
    {
        Create().ApplyEmptySetup();

        Assert.AreEqual(SetupChoice.Empty.ToString(), Choice(_db));
        Assert.AreEqual(0, new PendingQueueService(_db).CountPending());
        Assert.AreEqual(0, new LocalFileRepository(_db.Connection).GetAll().Count);
    }

    [TestMethod]
    public void NonEmptyFolder_ApplyEmpty_RefusesWithoutTouchingData()
    {
        File.WriteAllText(Path.Combine(_root, "keep.txt"), "keep-me");
        var queue = new PendingQueueService(_db);

        var ex = Assert.ThrowsExactly<InvalidOperationException>(() => Create().ApplyEmptySetup());

        Assert.IsTrue(ex.Message.Contains("already contains"));
        Assert.AreEqual("keep-me", File.ReadAllText(Path.Combine(_root, "keep.txt")),
            "Refused setup must not delete local data.");
        Assert.AreEqual(0, queue.CountPending(), "Refused setup must queue nothing.");
        Assert.IsNull(Choice(_db), "Refused setup must not record a choice.");
    }

    [TestMethod]
    public void NonEmptyFolder_ApplyImport_EnqueuesCreatesInDependencyOrder()
    {
        Directory.CreateDirectory(Path.Combine(_root, "Docs"));
        File.WriteAllText(Path.Combine(_root, "Docs", "note.txt"), "imported-bytes");
        File.WriteAllText(Path.Combine(_root, "top.txt"), "top-bytes");
        var queue = new PendingQueueService(_db);

        Create().ApplyImportSetup();

        Assert.AreEqual(SetupChoice.Import.ToString(), Choice(_db));
        Assert.AreEqual("imported-bytes", File.ReadAllText(Path.Combine(_root, "Docs", "note.txt")),
            "Import is additive: local files must survive.");
        var batch = queue.DequeueEligibleBatch(10);
        Assert.AreEqual(3, batch.Count);
        var folderIdx = batch.ToList().FindIndex(o => o.OpType == PendingOpType.CreateFolder && o.RelPath == "Docs");
        var fileIdx = batch.ToList().FindIndex(o => o.OpType == PendingOpType.CreateFile && o.RelPath == "Docs/note.txt");
        Assert.IsTrue(folderIdx >= 0 && fileIdx >= 0 && folderIdx < fileIdx,
            "Parent folder create must dequeue before child file create.");
        Assert.IsTrue(batch.All(o => o.OpType is PendingOpType.CreateFile or PendingOpType.CreateFolder),
            "Import enqueues fresh creates only, never deletes.");
        Assert.IsNull(new LocalFileRepository(_db.Connection).GetByPath("Docs/note.txt")?.ServerFileId,
            "Imported files have no server sequence; LWW does not apply.");
    }

    [TestMethod]
    public void Import_UnreadableFile_AbortsWithNothingQueued()
    {
        File.WriteAllText(Path.Combine(_root, "locked.txt"), "precious");
        var queue = new PendingQueueService(_db);
        var svc = Create(openRead: _ => throw new IOException("injected read failure"));

        Assert.ThrowsExactly<InvalidOperationException>(() => svc.ApplyImportSetup());

        Assert.AreEqual(0, queue.CountPending(), "Failed import must queue nothing.");
        Assert.IsNull(new LocalFileRepository(_db.Connection).GetByPath("locked.txt"),
            "Failed import must register no local rows.");
        Assert.IsNull(Choice(_db), "Failed import must not record a choice.");
        Assert.AreEqual("precious", File.ReadAllText(Path.Combine(_root, "locked.txt")));
    }

    [TestMethod]
    public void Import_IgnoresInternalRecoveryFiles()
    {
        var internalFile = Path.Combine(_root, ".nexsync", "Recovery", "DeletedByServer", "old.txt");
        Directory.CreateDirectory(Path.GetDirectoryName(internalFile)!);
        File.WriteAllText(internalFile, "recovered");
        File.WriteAllText(Path.Combine(_root, "real.txt"), "real-bytes");
        var queue = new PendingQueueService(_db);

        var inspection = Create().Inspect();
        Create().ApplyImportSetup();

        Assert.IsFalse(inspection.ExistingRelativePaths.Any(p => p.Contains(".nexsync")),
            "Internal recovery area must be invisible to setup inspection.");
        var batch = queue.DequeueEligibleBatch(10);
        Assert.AreEqual(1, batch.Count);
        Assert.AreEqual("real.txt", batch[0].RelPath);
    }
}
