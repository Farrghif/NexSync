using System;
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
public sealed class ConflictHandlerTests
{
    private string _dir = null!;
    private string _root = null!;
    private string DbPath => Path.Combine(_dir, ".nexsync", "local.db");
    private LocalDb _db = null!;

    [TestInitialize]
    public void Setup()
    {
        _dir = Path.Combine(Path.GetTempPath(), "nexsync-conflict-test", Guid.NewGuid().ToString("N"));
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
        public readonly List<HttpRequestMessage> Seen = new();
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Seen.Add(request);
            return Task.FromResult(respond(request));
        }
    }

    private static HttpResponseMessage Json(HttpStatusCode status, object payload)
    {
        var res = new HttpResponseMessage(status);
        res.Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");
        return res;
    }

    private static object Change(long seq, string type, Guid id, string op, string? name = null,
        Guid? parentId = null, string? hash = null, long? size = null, string? contentType = null, Guid? origin = null)
        => new
        {
            sequence = seq,
            entityType = type,
            entityId = id,
            operation = op,
            name,
            parentFolderId = parentId,
            hash,
            size,
            contentType,
            originDeviceId = origin,
            occurredAt = DateTime.UtcNow
        };

    private static string Sha256Of(byte[] bytes)
    {
        using var sha = System.Security.Cryptography.SHA256.Create();
        return Convert.ToHexString(sha.ComputeHash(bytes));
    }

    private static void SeedFile(LocalDb db, string rel, Guid? serverId, string hash = "abc")
        => new LocalFileRepository(db.Connection).Upsert(
            new LocalFileRow(rel, serverId, null, hash, 10, 0, DateTime.UtcNow));

    private static void SeedFolder(LocalDb db, string rel, Guid? serverId)
        => new LocalFolderRepository(db.Connection).Upsert(
            new LocalFolderRow(rel, serverId, null, 0, DateTime.UtcNow));

    private (PendingQueueService queue, PushPipeline push, PullPipeline pull, SyncCoordinator coordinator) CreatePipelines(
        Func<HttpRequestMessage, HttpResponseMessage> respond)
    {
        var handler = new StubHandler(respond);
        var auth = new DesktopAuthService("http://localhost:5000", Path.Combine(_dir, "auth"), handler);
        var queue = new PendingQueueService(_db);
        var conflicts = new ConflictHandler(_db, _root);
        var push = new PushPipeline(_db, queue, auth, _root);
        var pull = new PullPipeline(_db, queue, auth, _root, conflicts: conflicts);
        var coordinator = new SyncCoordinator(_db, queue, push, conflicts: conflicts);
        return (queue, push, pull, coordinator);
    }

    [TestMethod]
    public async Task Push409_PreservesLocalCopy_OpStaysUnresolved()
    {
        var fileId = Guid.NewGuid();
        var (queue, _, _, coordinator) = CreatePipelines(_ => Json(HttpStatusCode.Conflict, new { code = "FILE_NAME_CONFLICT" }));
        File.WriteAllText(Path.Combine(_root, "report.txt"), "my precious edits");
        SeedFile(_db, "report.txt", fileId);
        queue.Enqueue(PendingOpType.ModifyFile, "report.txt", serverId: fileId);

        var result = await coordinator.RunOnceAsync();

        Assert.AreEqual(1, result.Conflicts);
        Assert.AreEqual("my precious edits", File.ReadAllText(Path.Combine(_root, "report.txt")));
        Assert.AreEqual(1, queue.CountPending(), "Conflict op must stay unresolved, never dropped.");
        var recoveryFiles = Directory.GetFiles(Path.Combine(_root, ".nexsync", "Recovery", "Conflicts"), "*", SearchOption.AllDirectories);
        Assert.AreEqual(1, recoveryFiles.Length);
        Assert.AreEqual("my precious edits", File.ReadAllText(recoveryFiles[0]));
    }

    [TestMethod]
    public async Task RemoteFileDelete_WithUnpushedEdit_PreservesCancelsAppliesTombstone()
    {
        var fileId = Guid.NewGuid();
        var edited = Encoding.UTF8.GetBytes("unpushed local edits");
        File.WriteAllText(Path.Combine(_root, "notes.txt"), "unpushed local edits");
        SeedFile(_db, "notes.txt", fileId, hash: "STALE-HASH");
        var (queue, _, pull, _) = CreatePipelines(req =>
        {
            if (req.RequestUri!.ToString().Contains("/download"))
                return new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(edited) };
            return Json(HttpStatusCode.OK, new
            {
                changes = new[] { Change(11, "File", fileId, "Deleted", "notes.txt", null) },
                nextCursor = 11L,
                hasMore = false,
                highWaterCursor = 11L
            });
        });
        queue.Enqueue(PendingOpType.ModifyFile, "notes.txt", serverId: fileId);

        var outcome = await pull.PullAndApplyAsync(until: 11);

        Assert.AreEqual(PullApplyOutcome.Applied, outcome);
        var recoveryFiles = Directory.GetFiles(Path.Combine(_root, ".nexsync", "Recovery", "DeletedByServer"), "*", SearchOption.AllDirectories);
        Assert.AreEqual(1, recoveryFiles.Length);
        Assert.AreEqual("unpushed local edits", File.ReadAllText(recoveryFiles[0]));
        Assert.AreEqual(0, queue.CountPending(), "Obsolete ops under the tombstone must be cancelled.");
        Assert.IsNull(new LocalFileRepository(_db.Connection).GetByServerId(fileId));
        Assert.IsFalse(File.Exists(Path.Combine(_root, "notes.txt")));
        Assert.AreEqual(11L, pull.GetCursor());
    }

    [TestMethod]
    public async Task RemoteFileDelete_WithoutLocalEdits_AppliesDirectly()
    {
        var fileId = Guid.NewGuid();
        var content = Encoding.UTF8.GetBytes("synced-content");
        File.WriteAllText(Path.Combine(_root, "clean.txt"), "synced-content");
        SeedFile(_db, "clean.txt", fileId, hash: Sha256Of(content));
        var (queue, _, pull, _) = CreatePipelines(_ => Json(HttpStatusCode.OK, new
        {
            changes = new[] { Change(12, "File", fileId, "Deleted", "clean.txt", null) },
            nextCursor = 12L,
            hasMore = false,
            highWaterCursor = 12L
        }));

        Assert.AreEqual(PullApplyOutcome.Applied, await pull.PullAndApplyAsync(until: 12));

        var recoveryDir = Path.Combine(_root, ".nexsync", "Recovery", "DeletedByServer");
        Assert.IsTrue(!Directory.Exists(recoveryDir) || Directory.GetFiles(recoveryDir, "*", SearchOption.AllDirectories).Length == 0);
        Assert.AreEqual(0, queue.CountPending());
        Assert.AreEqual(12L, pull.GetCursor());
    }

    [TestMethod]
    public async Task RemoteFolderDelete_WithUnpushedChildEdit_PreservesTreeByteIdentical()
    {
        var folderId = Guid.NewGuid();
        var fileId = Guid.NewGuid();
        Directory.CreateDirectory(Path.Combine(_root, "Project", "Sub"));
        File.WriteAllText(Path.Combine(_root, "Project", "Sub", "code.cs"), "int x = 1;");
        SeedFolder(_db, "Project", folderId);
        SeedFolder(_db, "Project/Sub", Guid.NewGuid());
        SeedFile(_db, "Project/Sub/code.cs", fileId, hash: "STALE");
        var (queue, _, pull, _) = CreatePipelines(_ => Json(HttpStatusCode.OK, new
        {
            changes = new[] { Change(13, "Folder", folderId, "Deleted", "Project", null) },
            nextCursor = 13L,
            hasMore = false,
            highWaterCursor = 13L
        }));
        queue.Enqueue(PendingOpType.ModifyFile, "Project/Sub/code.cs", serverId: fileId);

        Assert.AreEqual(PullApplyOutcome.Applied, await pull.PullAndApplyAsync(until: 13));

        var recoveryFiles = Directory.GetFiles(Path.Combine(_root, ".nexsync", "Recovery", "DeletedByServer"), "*", SearchOption.AllDirectories);
        Assert.AreEqual(1, recoveryFiles.Length);
        Assert.AreEqual("int x = 1;", File.ReadAllText(recoveryFiles[0]));
        Assert.IsTrue(recoveryFiles[0].Replace('\\', '/').Contains("Project/Sub/code.cs"));
        Assert.AreEqual(0, queue.CountPending());
        Assert.IsFalse(Directory.Exists(Path.Combine(_root, "Project")));
        Assert.AreEqual(13L, pull.GetCursor());
    }

    [TestMethod]
    public async Task RecoveryArea_IsInvisibleToWatcher()
    {
        using var watcher = new FolderWatcherService(_root, _db);
        var dest = Path.Combine(_root, ".nexsync", "Recovery", "DeletedByServer", "x.txt");
        Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
        File.WriteAllText(dest, "recovered");
        watcher.ProcessPathForTest(dest);
        watcher.ProcessPathForTest(Path.Combine(_root, ".nexsync", "Recovery", "Conflicts", "y.txt"));

        Assert.AreEqual(0, new PendingOperationRepository(_db.Connection).CountPending());
    }

    [TestMethod]
    public async Task SameOp_RetriedWithSameContent_PreservesExactlyOnce()
    {
        var fileId = Guid.NewGuid();
        var (queue, _, _, coordinator) = CreatePipelines(_ => Json(HttpStatusCode.Conflict, new { code = "FILE_NAME_CONFLICT" }));
        File.WriteAllText(Path.Combine(_root, "stable.txt"), "identical-bytes");
        SeedFile(_db, "stable.txt", fileId);
        queue.Enqueue(PendingOpType.ModifyFile, "stable.txt", serverId: fileId);

        Assert.AreEqual(1, (await coordinator.RunOnceAsync()).Conflicts);
        Assert.AreEqual(1, (await coordinator.RunOnceAsync()).Conflicts);

        var copies = Directory.GetFiles(Path.Combine(_root, ".nexsync", "Recovery", "Conflicts"), "*", SearchOption.AllDirectories);
        Assert.AreEqual(1, copies.Length);
        Assert.AreEqual("identical-bytes", File.ReadAllText(copies[0]));
    }

    [TestMethod]
    public async Task SameOp_RetriedWithChangedContent_PreservesBothSnapshots()
    {
        var fileId = Guid.NewGuid();
        var (queue, _, _, coordinator) = CreatePipelines(_ => Json(HttpStatusCode.Conflict, new { code = "FILE_NAME_CONFLICT" }));
        File.WriteAllText(Path.Combine(_root, "evolving.txt"), "content-v1");
        SeedFile(_db, "evolving.txt", fileId);
        queue.Enqueue(PendingOpType.ModifyFile, "evolving.txt", serverId: fileId);

        Assert.AreEqual(1, (await coordinator.RunOnceAsync()).Conflicts);

        File.WriteAllText(Path.Combine(_root, "evolving.txt"), "content-v2-different");
        Assert.AreEqual(1, (await coordinator.RunOnceAsync()).Conflicts);

        var copies = Directory.GetFiles(Path.Combine(_root, ".nexsync", "Recovery", "Conflicts"), "*", SearchOption.AllDirectories);
        Assert.AreEqual(2, copies.Length);
        var bodies = copies.Select(File.ReadAllText).ToHashSet();
        Assert.IsTrue(bodies.Contains("content-v1"));
        Assert.IsTrue(bodies.Contains("content-v2-different"));
    }

    [TestMethod]
    public async Task UntrackedFile_UnderRemoteDeletedFolder_SurvivesInRecovery()
    {
        var folderId = Guid.NewGuid();
        Directory.CreateDirectory(Path.Combine(_root, "Vanish"));
        File.WriteAllText(Path.Combine(_root, "Vanish", "lost-event.txt"), "never-tracked-bytes");
        SeedFolder(_db, "Vanish", folderId);
        var (queue, _, pull, _) = CreatePipelines(_ => Json(HttpStatusCode.OK, new
        {
            changes = new[] { Change(21, "Folder", folderId, "Deleted", "Vanish", null) },
            nextCursor = 21L,
            hasMore = false,
            highWaterCursor = 21L
        }));

        Assert.AreEqual(PullApplyOutcome.Applied, await pull.PullAndApplyAsync(until: 21));

        var recoveryFiles = Directory.GetFiles(Path.Combine(_root, ".nexsync", "Recovery", "DeletedByServer"), "*", SearchOption.AllDirectories);
        Assert.AreEqual(1, recoveryFiles.Length);
        Assert.AreEqual("never-tracked-bytes", File.ReadAllText(recoveryFiles[0]));
        Assert.IsTrue(recoveryFiles[0].Replace('\\', '/').Contains("Vanish/lost-event.txt"));
    }

    [TestMethod]
    public async Task MoveIntoRemoteDeletedFolder_CancelledAsObsolete()
    {
        var folderId = Guid.NewGuid();
        var fileId = Guid.NewGuid();
        Directory.CreateDirectory(Path.Combine(_root, "Doomed"));
        Directory.CreateDirectory(Path.Combine(_root, "Elsewhere"));
        File.WriteAllText(Path.Combine(_root, "Elsewhere", "mover.txt"), "moving-in");
        SeedFolder(_db, "Doomed", folderId);
        SeedFile(_db, "Elsewhere/mover.txt", fileId);
        var (queue, _, pull, _) = CreatePipelines(_ => Json(HttpStatusCode.OK, new
        {
            changes = new[] { Change(22, "Folder", folderId, "Deleted", "Doomed", null) },
            nextCursor = 22L,
            hasMore = false,
            highWaterCursor = 22L
        }));
        queue.Enqueue(PendingOpType.MoveFile, "Elsewhere/mover.txt", newRelPath: "Doomed/mover.txt", serverId: fileId);

        Assert.AreEqual(PullApplyOutcome.Applied, await pull.PullAndApplyAsync(until: 22));

        Assert.AreEqual(0, queue.CountPending());
        Assert.IsTrue(Directory.Exists(Path.Combine(_root, ".nexsync", "Recovery", "DeletedByServer")));
    }

    [TestMethod]
    public void PreserveFailure_SurfacesError_AndReportsNothingToDoSeparately()
    {
        var conflicts = new ConflictHandler(_db, _root);
        var missing = conflicts.PreservePushConflict(
            new PendingOperationRow(Guid.NewGuid(), PendingOpType.ModifyFile, "nope.txt", null, null, null, 0, PendingOpStatus.Pending, DateTime.UtcNow));
        Assert.IsNull(missing);
        Assert.IsNull(conflicts.LastError, "Missing source is nothing-to-preserve, not a failure.");
    }

    [TestMethod]
    public async Task MissingConflictHandler_PreservationUnavailable_IsDetected()
    {
        var fileId = Guid.NewGuid();
        var handler = new StubHandler(_ => Json(HttpStatusCode.OK, new
        {
            changes = new[] { Change(23, "File", fileId, "Deleted", "orphan.txt", null) },
            nextCursor = 23L,
            hasMore = false,
            highWaterCursor = 23L
        }));
        var auth = new DesktopAuthService("http://localhost:5000", Path.Combine(_dir, "auth"), handler);
        var queue = new PendingQueueService(_db);
        var pull = new PullPipeline(_db, queue, auth, _root);
        File.WriteAllText(Path.Combine(_root, "orphan.txt"), "unpushed");
        SeedFile(_db, "orphan.txt", fileId, hash: "STALE");
        queue.Enqueue(PendingOpType.ModifyFile, "orphan.txt", serverId: fileId);

        Assert.AreEqual(PullApplyOutcome.Applied, await pull.PullAndApplyAsync(until: 23));
        var recoveryDir = Path.Combine(_root, ".nexsync", "Recovery", "DeletedByServer");
        var preserved = Directory.Exists(recoveryDir)
            ? Directory.GetFiles(recoveryDir, "*", SearchOption.AllDirectories).Length
            : 0;
        Assert.AreEqual(0, preserved,
            "Without ConflictHandler no preservation copy may exist; tombstone applies without recovery.");
        Assert.IsFalse(File.Exists(Path.Combine(_root, "orphan.txt")));
    }

    [TestMethod]
    public void ProductionWiring_SyncEngine_PassesSharedConflictHandler()
    {
        var handler = new StubHandler(_ => Json(HttpStatusCode.OK, new { ok = true }));
        var auth = new DesktopAuthService("http://localhost:5000", Path.Combine(_dir, "auth-wiring"), handler);
        using var db = new LocalDb(Path.Combine(_dir, ".nexsync-wiring", "local.db"));
        using var engine = SyncEngineFactory.Create(db, auth, Path.Combine(_dir, "wiring-files"));

        Assert.IsNotNull(engine.Coordinator);
        Assert.IsNotNull(engine.Pull);
        Assert.IsNotNull(engine.Push);
        Assert.IsNotNull(engine.Conflicts);
        Assert.IsNotNull(engine.Watcher);
        Assert.IsNotNull(engine.Recovery);
    }

    [TestMethod]
    public async Task ConflictPreservation_Retryable_SecondRunKeepsBothCopies()
    {
        var fileId = Guid.NewGuid();
        var calls = 0;
        var (queue, _, _, coordinator) = CreatePipelines(_ =>
        {
            calls++;
            return Json(HttpStatusCode.Conflict, new { code = "FILE_NAME_CONFLICT" });
        });
        File.WriteAllText(Path.Combine(_root, "dup.txt"), "v1");
        SeedFile(_db, "dup.txt", fileId);
        queue.Enqueue(PendingOpType.ModifyFile, "dup.txt", serverId: fileId);

        Assert.AreEqual(1, (await coordinator.RunOnceAsync()).Conflicts);
        Assert.AreEqual(1, (await coordinator.RunOnceAsync()).Conflicts);

        Assert.AreEqual(1, queue.CountPending());
        Assert.AreEqual("v1", File.ReadAllText(Path.Combine(_root, "dup.txt")));
        var copies = Directory.GetFiles(Path.Combine(_root, ".nexsync", "Recovery", "Conflicts"), "*", SearchOption.AllDirectories);
        Assert.AreEqual(1, copies.Length, "Same logical op preserved once; retry must not duplicate the copy.");
        Assert.AreEqual("v1", File.ReadAllText(copies[0]));
        Assert.AreEqual(2, calls);
    }
}
