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

        var first = await coordinator.RunOnceAsync();
        var second = await coordinator.RunOnceAsync();
        Assert.AreEqual(1, first.Conflicts);
        Assert.AreEqual(1, second.Conflicts);
        Assert.AreEqual(0, first.PreservationErrors.Count, "Successful preservation must report no errors.");
        Assert.AreEqual(0, second.PreservationErrors.Count, "Idempotent retry must report no errors.");

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
    public void PushConflict_PreservationFailure_SetsLastError()
    {
        var fileId = Guid.NewGuid();
        var lockedPath = Path.Combine(_root, "locked.txt");
        File.WriteAllText(lockedPath, "precious");
        SeedFile(_db, "locked.txt", fileId);
        var queue = new PendingQueueService(_db);
        var opId = queue.Enqueue(PendingOpType.ModifyFile, "locked.txt", serverId: fileId);
        var conflicts = new ConflictHandler(_db, _root);
        var op = conflicts.GetOpsFor("locked.txt").First(o => o.OperationId == opId);

        string? directResult;
        using (var blocker = new FileStream(lockedPath, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
        {
            directResult = conflicts.PreservePushConflict(op);
        }

        Assert.IsNull(directResult);
        Assert.IsNotNull(conflicts.LastError);
        Assert.IsTrue(conflicts.LastError.Contains("locked.txt"));
        var conflictsDir = Path.Combine(_root, ".nexsync", "Recovery", "Conflicts");
        Assert.IsTrue(!Directory.Exists(conflictsDir)
            || Directory.GetFiles(conflictsDir, "*", SearchOption.AllDirectories).Length == 0,
            "Failed preservation must leave no artifact behind.");
        Assert.AreEqual(0, Directory.GetFiles(_root, "*.staging-*", SearchOption.AllDirectories).Length,
            "Staging leftovers must be cleaned up on failure.");
    }

    [TestMethod]
    public async Task Coordinator_SurfacesPreservationFailure_OpStaysUnresolved()
    {
        var fileId = Guid.NewGuid();
        File.WriteAllText(Path.Combine(_root, "fragile.txt"), "fragile-bytes");
        SeedFile(_db, "fragile.txt", fileId);
        var handler = new StubHandler(_ => Json(HttpStatusCode.Conflict, new { code = "FILE_NAME_CONFLICT" }));
        var auth = new DesktopAuthService("http://localhost:5000", Path.Combine(_dir, "auth"), handler);
        var queue = new PendingQueueService(_db);
        var push = new PushPipeline(_db, queue, auth, _root);
        var coordinator = new SyncCoordinator(_db, queue, push, new ConflictHandler(_db, _root), preserveConflict: _ => null);
        queue.Enqueue(PendingOpType.ModifyFile, "fragile.txt", serverId: fileId);

        var result = await coordinator.RunOnceAsync();

        Assert.AreEqual(1, result.Conflicts);
        Assert.AreEqual(1, result.PreservationErrors.Count);
        Assert.IsTrue(result.PreservationErrors[0].Contains("fragile.txt"));
        Assert.AreEqual(1, queue.CountPending(), "Op must stay unresolved when preservation fails.");
        Assert.AreEqual("fragile-bytes", File.ReadAllText(Path.Combine(_root, "fragile.txt")));
    }

    [TestMethod]
    public async Task Coordinator_RealHandlerFailure_CollectsLastError_OpStaysUnresolved()
    {
        var fileId = Guid.NewGuid();
        var content = Encoding.UTF8.GetBytes("real-bytes");
        File.WriteAllBytes(Path.Combine(_root, "reallock.txt"), content);
        SeedFile(_db, "reallock.txt", fileId);
        var (queue, _, _, coordinator) = CreatePipelines(_ => Json(HttpStatusCode.Conflict, new { code = "FILE_NAME_CONFLICT" }));
        queue.Enqueue(PendingOpType.ModifyFile, "reallock.txt", serverId: fileId);

        string fingerprint;
        using (var sha = System.Security.Cryptography.SHA256.Create())
            fingerprint = Convert.ToHexString(sha.ComputeHash(content));
        var destFile = Path.Combine(_root, ".nexsync", "Recovery", "Conflicts", fingerprint, "reallock.txt");
        Directory.CreateDirectory(Path.GetDirectoryName(destFile)!);
        Directory.CreateDirectory(destFile);

        var result = await coordinator.RunOnceAsync();

        Directory.Delete(destFile, recursive: false);

        Assert.AreEqual(1, result.Conflicts);
        Assert.AreEqual(1, result.PreservationErrors.Count);
        Assert.IsTrue(result.PreservationErrors[0].Contains("reallock.txt"));
        Assert.AreEqual(1, queue.CountPending(), "Op must stay unresolved when preservation fails.");
        Assert.AreEqual("real-bytes", File.ReadAllText(Path.Combine(_root, "reallock.txt")));
    }

    [TestMethod]
    public async Task PullPreserveFailure_AbortsTombstone_CursorFrozen()
    {
        var fileId = Guid.NewGuid();
        var victimPath = Path.Combine(_root, "victim.txt");
        File.WriteAllText(victimPath, "victim-bytes");
        SeedFile(_db, "victim.txt", fileId, hash: "STALE-HASH");
        var (queue, _, pull, _) = CreatePipelines(req =>
        {
            if (req.RequestUri!.ToString().Contains("/download"))
                return new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(Array.Empty<byte>()) };
            return Json(HttpStatusCode.OK, new
            {
                changes = new[] { Change(41, "File", fileId, "Deleted", "victim.txt", null) },
                nextCursor = 41L,
                hasMore = false,
                highWaterCursor = 41L
            });
        });
        queue.Enqueue(PendingOpType.ModifyFile, "victim.txt", serverId: fileId);
        var cursorBefore = pull.GetCursor();

        PullApplyOutcome outcome;
        using (var blocker = new FileStream(victimPath, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
        {
            outcome = await pull.PullAndApplyAsync(until: 41);
        }

        Assert.AreEqual(PullApplyOutcome.TransientFailure, outcome);
        Assert.AreEqual(cursorBefore, pull.GetCursor(), "Cursor must not advance when preservation fails.");
        Assert.IsTrue(File.Exists(victimPath), "Source must survive an aborted tombstone.");
        Assert.AreEqual(1, queue.CountPending(), "Ops must not be cancelled when preservation fails.");
    }

    [TestMethod]
    public void HasUnpushedChanges_IoFailure_IsFailClosed()
    {
        var content = Encoding.UTF8.GetBytes("synced-bytes");
        File.WriteAllBytes(Path.Combine(_root, "io-fail.txt"), content);
        SeedFile(_db, "io-fail.txt", Guid.NewGuid(), hash: Sha256Of(content));
        var conflicts = new ConflictHandler(_db, _root, openRead: _ => throw new IOException("injected read failure"));

        Assert.IsTrue(conflicts.HasUnpushedChanges("io-fail.txt"),
            "Unreadable file must be treated as potentially unpushed, never as safe-to-delete.");
    }

    [TestMethod]
    public async Task RemoteDelete_IoFailure_AbortsTombstone_PreservesFile()
    {
        var fileId = Guid.NewGuid();
        var content = Encoding.UTF8.GetBytes("original-bytes");
        File.WriteAllBytes(Path.Combine(_root, "precious.txt"), content);
        SeedFile(_db, "precious.txt", fileId, hash: Sha256Of(content));
        var handler = new StubHandler(_ => Json(HttpStatusCode.OK, new
        {
            changes = new[] { Change(51, "File", fileId, "Deleted", "precious.txt", null) },
            nextCursor = 51L,
            hasMore = false,
            highWaterCursor = 51L
        }));
        var auth = new DesktopAuthService("http://localhost:5000", Path.Combine(_dir, "auth-iofail"), handler);
        var queue = new PendingQueueService(_db);
        var conflicts = new ConflictHandler(_db, _root, openRead: _ => throw new IOException("injected read failure"));
        var pull = new PullPipeline(_db, queue, auth, _root, conflicts: conflicts);
        var cursorBefore = pull.GetCursor();

        var outcome = await pull.PullAndApplyAsync(until: 51);

        Assert.AreEqual(PullApplyOutcome.TransientFailure, outcome);
        Assert.AreEqual(cursorBefore, pull.GetCursor(), "Cursor must not advance past a failed tombstone.");
        Assert.IsTrue(File.Exists(Path.Combine(_root, "precious.txt")), "Original must survive an aborted tombstone.");
        Assert.AreEqual("original-bytes", File.ReadAllText(Path.Combine(_root, "precious.txt")));
        Assert.IsNotNull(new LocalFileRepository(_db.Connection).GetByServerId(fileId), "DB row must survive an aborted tombstone.");
        Assert.IsNotNull(conflicts.LastError, "Preservation failure must be reported explicitly.");
    }

    [TestMethod]
    public async Task CancelOpsUnder_UnrelatedOpsSurvive()
    {
        var folderId = Guid.NewGuid();
        Directory.CreateDirectory(Path.Combine(_root, "Gone"));
        Directory.CreateDirectory(Path.Combine(_root, "Stay"));
        File.WriteAllText(Path.Combine(_root, "Gone", "a.txt"), "a");
        File.WriteAllText(Path.Combine(_root, "Stay", "b.txt"), "b");
        SeedFolder(_db, "Gone", folderId);
        SeedFolder(_db, "Stay", Guid.NewGuid());
        SeedFile(_db, "Gone/a.txt", Guid.NewGuid());
        SeedFile(_db, "Stay/b.txt", Guid.NewGuid());
        var (queue, _, pull, _) = CreatePipelines(_ => Json(HttpStatusCode.OK, new
        {
            changes = new[] { Change(42, "Folder", folderId, "Deleted", "Gone", null) },
            nextCursor = 42L,
            hasMore = false,
            highWaterCursor = 42L
        }));
        queue.Enqueue(PendingOpType.ModifyFile, "Gone/a.txt", serverId: Guid.NewGuid());
        queue.Enqueue(PendingOpType.ModifyFile, "Stay/b.txt", serverId: Guid.NewGuid());

        Assert.AreEqual(PullApplyOutcome.Applied, await pull.PullAndApplyAsync(until: 42));

        var remaining = queue.DequeueEligibleBatch(10);
        Assert.AreEqual(1, remaining.Count);
        Assert.AreEqual("Stay/b.txt", remaining[0].RelPath);
        Assert.AreEqual("b", File.ReadAllText(Path.Combine(_root, "Stay", "b.txt")));
    }

    [TestMethod]
    public void MissingConflictHandler_PullPipelineRefusesToConstruct()
    {
        var handler = new StubHandler(_ => Json(HttpStatusCode.OK, new { ok = true }));
        var auth = new DesktopAuthService("http://localhost:5000", Path.Combine(_dir, "auth"), handler);
        var queue = new PendingQueueService(_db);

        var ex = Assert.ThrowsExactly<ArgumentNullException>(() =>
            new PullPipeline(_db, queue, auth, _root, conflicts: null));
        Assert.AreEqual("conflicts", ex.ParamName);
    }

    [TestMethod]
    public void ProductionWiring_SyncEngine_ExposesSingleSharedHandler()
    {
        var handler = new StubHandler(_ => Json(HttpStatusCode.OK, new { ok = true }));
        var auth = new DesktopAuthService("http://localhost:5000", Path.Combine(_dir, "auth-wiring2"), handler);
        using var db = new LocalDb(Path.Combine(_dir, ".nexsync-wiring2", "local.db"));
        using var engine = SyncEngineFactory.Create(db, auth, Path.Combine(_dir, "wiring2-files"));

        Assert.IsNotNull(engine.Conflicts);
        Assert.AreSame(engine.Conflicts, engine.Pull.ActiveConflictHandler);
        Assert.AreSame(engine.Conflicts, engine.Coordinator.ActiveConflictHandler);
    }

    [TestMethod]
    public async Task ProductionWiring_SyncEngine_SharesOneConflictHandlerEndToEnd()
    {
        var fileId = Guid.NewGuid();
        var wiringRoot = Path.Combine(_dir, "wiring-files");
        var handler = new StubHandler(req =>
        {
            var url = req.RequestUri!.ToString();
            if (url.Contains("/api/files/upload") && req.Method == HttpMethod.Post)
                return Json(HttpStatusCode.Conflict, new { code = "FILE_NAME_CONFLICT" });
            if (url.Contains("/api/files/") && req.Method == HttpMethod.Post)
                return Json(HttpStatusCode.Created, new
                {
                    id = fileId,
                    name = "shared.txt",
                    hash = "abc",
                    size = 13L,
                    contentType = "text/plain"
                });
            return Json(HttpStatusCode.OK, new
            {
                changes = new[] { Change(31, "File", fileId, "Deleted", "shared.txt", null) },
                nextCursor = 31L,
                hasMore = false,
                highWaterCursor = 31L
            });
        });
        var auth = new DesktopAuthService("http://localhost:5000", Path.Combine(_dir, "auth-wiring"), handler);
        using var db = new LocalDb(Path.Combine(_dir, ".nexsync-wiring", "local.db"));
        using var engine = SyncEngineFactory.Create(db, auth, wiringRoot);

        Directory.CreateDirectory(wiringRoot);
        File.WriteAllText(Path.Combine(wiringRoot, "shared.txt"), "wired-content");
        new LocalFileRepository(db.Connection).Upsert(
            new LocalFileRow("shared.txt", fileId, null, "STALE", 13, 0, DateTime.UtcNow));
        engine.Queue.Enqueue(PendingOpType.ModifyFile, "shared.txt", serverId: fileId);

        var pushResult = await engine.Coordinator.RunOnceAsync();
        Assert.AreEqual(1, pushResult.Conflicts);
        var conflictCopies = Directory.GetFiles(
            Path.Combine(wiringRoot, ".nexsync", "Recovery", "Conflicts"), "*", SearchOption.AllDirectories);
        Assert.AreEqual(1, conflictCopies.Length);

        var pullOutcome = await engine.Pull.PullAndApplyAsync(until: 31);
        Assert.AreEqual(PullApplyOutcome.Applied, pullOutcome);
        var deletedCopies = Directory.GetFiles(
            Path.Combine(wiringRoot, ".nexsync", "Recovery", "DeletedByServer"), "*", SearchOption.AllDirectories);
        Assert.AreEqual(1, deletedCopies.Length);
        Assert.AreEqual("wired-content", File.ReadAllText(deletedCopies[0]));
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
