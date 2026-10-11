using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NexSync.Desktop.Auth;
using NexSync.Desktop.Data;
using NexSync.Desktop.Sync;

namespace NexSync.Desktop.Tests;

[TestClass]
public sealed class RecoveryServiceTests
{
    private string _dir = null!;
    private string _root = null!;
    private string DbPath => Path.Combine(_dir, ".nexsync", "local.db");
    private LocalDb _db = null!;

    [TestInitialize]
    public void Setup()
    {
        _dir = Path.Combine(Path.GetTempPath(), "nexsync-recovery-test", Guid.NewGuid().ToString("N"));
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

    private sealed class StubHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
            => Task.FromResult(respond(request));
    }

    private static HttpResponseMessage Json(HttpStatusCode status, object payload)
    {
        var res = new HttpResponseMessage(status);
        res.Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");
        return res;
    }

    private static object ManifestFolder(Guid id, string name, Guid? parentId) =>
        new { id, name, parentFolderId = parentId };

    private static object ManifestFile(Guid id, string name, Guid? folderId, string hash, long size) =>
        new { id, name, folderId, hash, size, contentType = "application/octet-stream" };

    private static string Sha256Of(byte[] bytes)
    {
        using var sha = System.Security.Cryptography.SHA256.Create();
        return Convert.ToHexString(sha.ComputeHash(bytes));
    }

    private RecoveryService Create(Func<HttpRequestMessage, HttpResponseMessage> respond)
    {
        var handler = new StubHandler(respond);
        var auth = new DesktopAuthService("http://localhost:5000", Path.Combine(_dir, "auth"), handler);
        return new RecoveryService(_db, new PendingQueueService(_db), auth, _root);
    }

    private static void SeedFolder(LocalDb db, string rel, Guid? serverId)
        => new LocalFolderRepository(db.Connection).Upsert(
            new LocalFolderRow(rel, serverId, null, 0, DateTime.UtcNow));

    private static void SeedFile(LocalDb db, string rel, Guid? serverId, string hash = "abc")
        => new LocalFileRepository(db.Connection).Upsert(
            new LocalFileRow(rel, serverId, null, hash, 10, 0, DateTime.UtcNow));

    private static List<PendingOperationRow> Pending(LocalDb db)
        => new PendingOperationRepository(db.Connection).DequeueBatch(1000);

    [TestMethod]
    public async Task OverflowMidBurst_ConvergesLocalToServer()
    {
        var folderId = Guid.NewGuid();
        var keptId = Guid.NewGuid();
        var keptBytes = Encoding.UTF8.GetBytes("kept-content");
        Directory.CreateDirectory(Path.Combine(_root, "Docs"));
        File.WriteAllText(Path.Combine(_root, "Docs", "kept.txt"), "kept-content");

        var svc = Create(_ => Json(HttpStatusCode.OK, new
        {
            snapshotCursor = 42L,
            folders = new[] { ManifestFolder(folderId, "Docs", null) },
            files = new[] { ManifestFile(keptId, "kept.txt", folderId, Sha256Of(keptBytes), keptBytes.Length) }
        }));

        var outcome = await svc.ReconcileAsync();

        Assert.AreEqual(ReconcileOutcome.Converged, outcome);
        var folders = new LocalFolderRepository(_db.Connection).GetAll();
        Assert.AreEqual(1, folders.Count);
        Assert.AreEqual(folderId, folders[0].ServerFolderId);
        Assert.AreEqual("Docs", folders[0].RelativePath);
        var files = new LocalFileRepository(_db.Connection).GetAll();
        Assert.AreEqual(1, files.Count);
        Assert.AreEqual(keptId, files[0].ServerFileId);
        Assert.AreEqual(folderId, files[0].ServerFolderId);
        Assert.AreEqual(42L, new SyncStateRepository(_db.Connection).GetSequence("LastPulledSequence"));
        Assert.AreEqual(0, Pending(_db).Count(o => o.OpType == PendingOpType.CreateFile && o.RelPath == "kept.txt"),
            "Hash-identical file must be adopted, not re-uploaded.");
    }

    [TestMethod]
    public async Task DeletedLocalDb_FullRebuild_SetsCursor()
    {
        _db.Dispose();
        File.Delete(DbPath);
        _db = new LocalDb(DbPath);
        Directory.CreateDirectory(Path.Combine(_root, "Work"));
        var payload = Encoding.UTF8.GetBytes("local-only");
        File.WriteAllBytes(Path.Combine(_root, "Work", "note.bin"), payload);
        var folderId = Guid.NewGuid();

        var svc = Create(_ => Json(HttpStatusCode.OK, new
        {
            snapshotCursor = 7L,
            folders = new[] { ManifestFolder(folderId, "Work", null) },
            files = Array.Empty<object>()
        }));

        var outcome = await svc.ReconcileAsync();

        Assert.AreEqual(ReconcileOutcome.Converged, outcome);
        var file = new LocalFileRepository(_db.Connection).GetByPath("Work/note.bin");
        Assert.IsNotNull(file);
        Assert.AreEqual(Sha256Of(payload), file.Hash);
        var ops = Pending(_db);
        Assert.AreEqual(1, ops.Count(o => o.OpType == PendingOpType.CreateFile && o.RelPath == "Work/note.bin"));
        Assert.AreEqual(7L, new SyncStateRepository(_db.Connection).GetSequence("LastPulledSequence"));
    }

    [TestMethod]
    public async Task LocallyModifiedFile_EnqueuesModify_NotCreate()
    {
        var fileId = Guid.NewGuid();
        var serverBytes = Encoding.UTF8.GetBytes("server-version");
        File.WriteAllText(Path.Combine(_root, "edit.txt"), "locally-modified-version");
        var svc = Create(_ => Json(HttpStatusCode.OK, new
        {
            snapshotCursor = 3L,
            folders = Array.Empty<object>(),
            files = new[] { ManifestFile(fileId, "edit.txt", null, Sha256Of(serverBytes), serverBytes.Length) }
        }));

        Assert.AreEqual(ReconcileOutcome.Converged, await svc.ReconcileAsync());

        var ops = Pending(_db);
        Assert.AreEqual(1, ops.Count);
        Assert.AreEqual(PendingOpType.ModifyFile, ops[0].OpType);
        Assert.AreEqual(fileId, ops[0].ServerId);
    }

    [TestMethod]
    public async Task LocallyDeletedFile_WithServerCopy_EnqueuesDelete()
    {
        var fileId = Guid.NewGuid();
        SeedFile(_db, "gone.txt", fileId, hash: "OLDHASH");
        var svc = Create(_ => Json(HttpStatusCode.OK, new
        {
            snapshotCursor = 5L,
            folders = Array.Empty<object>(),
            files = new[] { ManifestFile(fileId, "gone.txt", null, "OLDHASH", 3) }
        }));

        Assert.AreEqual(ReconcileOutcome.Converged, await svc.ReconcileAsync());

        var ops = Pending(_db);
        Assert.AreEqual(1, ops.Count);
        Assert.AreEqual(PendingOpType.DeleteFile, ops[0].OpType);
        Assert.AreEqual(fileId, ops[0].ServerId);
    }

    [TestMethod]
    public async Task ManifestFetchFailure_KeepsFlag_PendingAndCursorUntouched()
    {
        SeedFolder(_db, "Keep", Guid.NewGuid());
        var queue = new PendingQueueService(_db);
        queue.Enqueue(PendingOpType.CreateFolder, "Keep");
        new SyncStateRepository(_db.Connection).Set("LastPulledSequence", "9");
        var svc = Create(_ => new HttpResponseMessage(HttpStatusCode.InternalServerError));

        Assert.AreEqual(ReconcileOutcome.TransientFailure, await svc.ReconcileAsync());

        Assert.AreEqual(1, Pending(_db).Count);
        Assert.AreEqual(9L, new SyncStateRepository(_db.Connection).GetSequence("LastPulledSequence"));
        Assert.IsNotNull(new LocalFolderRepository(_db.Connection).GetByPath("Keep"));

        var svc2 = Create(_ => Json(HttpStatusCode.OK, new
        {
            snapshotCursor = 10L,
            folders = Array.Empty<object>(),
            files = Array.Empty<object>()
        }));
        Assert.AreEqual(ReconcileOutcome.Converged, await svc2.ReconcileAsync());
        Assert.AreEqual(10L, new SyncStateRepository(_db.Connection).GetSequence("LastPulledSequence"));
    }

    [TestMethod]
    public async Task AuthFailures_SurfaceWithoutStateChange()
    {
        var svc403 = Create(_ => new HttpResponseMessage(HttpStatusCode.Forbidden));
        Assert.AreEqual(ReconcileOutcome.Forbidden, await svc403.ReconcileAsync());

        var svc401 = Create(_ => new HttpResponseMessage(HttpStatusCode.Unauthorized));
        Assert.AreEqual(ReconcileOutcome.AuthFailed, await svc401.ReconcileAsync());

        Assert.AreEqual(0L, new SyncStateRepository(_db.Connection).GetSequence("LastPulledSequence"));
    }

    [TestMethod]
    public async Task RerunAfterSuccess_IsStable_NoOpsRegenerated()
    {
        var folderId = Guid.NewGuid();
        var svc = Create(_ => Json(HttpStatusCode.OK, new
        {
            snapshotCursor = 11L,
            folders = new[] { ManifestFolder(folderId, "Stable", null) },
            files = Array.Empty<object>()
        }));

        Assert.AreEqual(ReconcileOutcome.Converged, await svc.ReconcileAsync());
        Assert.AreEqual(0, Pending(_db).Count);

        var svc2 = Create(_ => Json(HttpStatusCode.OK, new
        {
            snapshotCursor = 11L,
            folders = new[] { ManifestFolder(folderId, "Stable", null) },
            files = Array.Empty<object>()
        }));
        Assert.AreEqual(ReconcileOutcome.Converged, await svc2.ReconcileAsync());
        Assert.AreEqual(0, Pending(_db).Count);
        Assert.AreEqual(11L, new SyncStateRepository(_db.Connection).GetSequence("LastPulledSequence"));
    }

    [TestMethod]
    public async Task SwallowedOverflow_WithoutReconcile_DivergenceRemainsDetectable()
    {
        var folderId = Guid.NewGuid();
        File.WriteAllText(Path.Combine(_root, "missed.txt"), "missed-by-overflow");
        var svc = Create(_ => Json(HttpStatusCode.OK, new
        {
            snapshotCursor = 20L,
            folders = new[] { ManifestFolder(folderId, "Docs", null) },
            files = Array.Empty<object>()
        }));

        Assert.AreEqual(ReconcileOutcome.Converged, await svc.ReconcileAsync());
        var ops = Pending(_db);
        var create = ops.FirstOrDefault(o => o.OpType == PendingOpType.CreateFile && o.RelPath == "missed.txt");
        Assert.IsNotNull(create, "Missed local file must be re-observed as CreateFile; swallowing the overflow would leave zero ops.");
        var row = new LocalFileRepository(_db.Connection).GetByPath("missed.txt");
        Assert.IsNotNull(row);
        Assert.IsNull(row.ServerFileId);
    }

    [TestMethod]
    public async Task ServerMovedFile_UpdatesLocalRow_NoDuplicateOp()
    {
        var fileId = Guid.NewGuid();
        var oldId = Guid.NewGuid();
        var newId = Guid.NewGuid();
        SeedFolder(_db, "Old", oldId);
        SeedFolder(_db, "New", newId);
        Directory.CreateDirectory(Path.Combine(_root, "Old"));
        File.WriteAllText(Path.Combine(_root, "Old", "m.txt"), "data");
        SeedFile(_db, "Old/m.txt", fileId);
        var svc = Create(_ => Json(HttpStatusCode.OK, new
        {
            snapshotCursor = 12L,
            folders = new[]
            {
                ManifestFolder(oldId, "Old", null),
                ManifestFolder(newId, "New", null)
            },
            files = new[] { ManifestFile(fileId, "m.txt", newId, Sha256Of(Encoding.UTF8.GetBytes("data")), 4) }
        }));

        Assert.AreEqual(ReconcileOutcome.Converged, await svc.ReconcileAsync());

        var rows = new LocalFileRepository(_db.Connection).GetAll()
            .Where(f => f.ServerFileId == fileId).ToList();
        Assert.AreEqual(1, rows.Count);
        Assert.AreEqual("New/m.txt", rows[0].RelativePath);
        Assert.IsTrue(File.Exists(Path.Combine(_root, "New", "m.txt")));
        Assert.AreEqual(0, Pending(_db).Count);
    }
}
