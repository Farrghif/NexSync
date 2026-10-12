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
public sealed class PushPipelineTests
{
    private string _dir = null!;
    private string _root = null!;
    private string DbPath => Path.Combine(_dir, ".nexsync", "local.db");
    private LocalDb _db = null!;

    [TestInitialize]
    public void Setup()
    {
        _dir = Path.Combine(Path.GetTempPath(), "nexsync-push-test", Guid.NewGuid().ToString("N"));
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

    private sealed class StubHandler(Func<HttpRequestMessage, Task<HttpResponseMessage>> respond) : HttpMessageHandler
    {
        public StubHandler(Func<HttpRequestMessage, HttpResponseMessage> sync) : this(req => Task.FromResult(sync(req)))
        {
        }

        public readonly List<HttpRequestMessage> Seen = new();
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Seen.Add(request);
            return respond(request);
        }
    }

    private static HttpResponseMessage Json(HttpStatusCode status, object payload)
    {
        var res = new HttpResponseMessage(status);
        res.Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");
        return res;
    }

    private (PendingQueueService queue, PushPipeline push, StubHandler handler) Create(
        Func<HttpRequestMessage, HttpResponseMessage> respond)
    {
        var handler = new StubHandler(respond);
        var auth = new DesktopAuthService("http://localhost:5000", Path.Combine(_dir, "auth"), handler);
        var queue = new PendingQueueService(_db);
        var push = new PushPipeline(_db, queue, auth, _root);
        return (queue, push, handler);
    }

    private static PendingOperationRow Op(PendingOpType type, string path, string? newPath = null, Guid? serverId = null)
        => new(Guid.NewGuid(), type, path, newPath, serverId, null, 0, PendingOpStatus.Pending, DateTime.UtcNow);

    private static void SeedFile(LocalDb db, string rel, Guid? serverId, string hash = "abc")
        => new LocalFileRepository(db.Connection).Upsert(
            new LocalFileRow(rel, serverId, null, hash, 10, 0, DateTime.UtcNow));

    private static void SeedFolder(LocalDb db, string rel, Guid? serverId)
        => new LocalFolderRepository(db.Connection).Upsert(
            new LocalFolderRow(rel, serverId, null, 0, DateTime.UtcNow));

    [TestMethod]
    public async Task CreateFolder_SendsHeaders_UpdatesServerId_CompletesOp()
    {
        HttpRequestMessage? seen = null;
        var serverId = Guid.NewGuid();
        var (queue, push, handler) = Create(req =>
        {
            seen = req;
            return Json(HttpStatusCode.Created, new { id = serverId, name = "Projects" });
        });
        var opId = queue.Enqueue(PendingOpType.CreateFolder, "Projects");
        var batch = queue.DequeueEligibleBatch(10);
        Assert.AreEqual(1, batch.Count);

        var outcome = await push.PushAsync(batch[0]);

        Assert.AreEqual(PushOutcome.Pushed, outcome);
        Assert.IsNotNull(seen);
        Assert.IsTrue(seen.Headers.Contains("X-Operation-Id"));
        Assert.AreEqual(opId.ToString(), string.Join(",", seen.Headers.GetValues("X-Operation-Id")));
        Assert.AreEqual(0, queue.CountPending());
        var row = new LocalFolderRepository(_db.Connection).GetByPath("Projects");
        Assert.AreEqual(serverId, row?.ServerFolderId);
    }

    [TestMethod]
    public async Task Upload_SendsMultipartWithOperationId_StoresHash()
    {
        var serverId = Guid.NewGuid();
        var (queue, push, _) = Create(_ => Json(HttpStatusCode.Created, new
        {
            id = serverId,
            name = "a.txt",
            hash = "HASH123",
            size = 11L,
            contentType = "application/octet-stream"
        }));
        File.WriteAllText(Path.Combine(_root, "a.txt"), "hello world");
        queue.Enqueue(PendingOpType.CreateFile, "a.txt");
        var batch = queue.DequeueEligibleBatch(10);

        var outcome = await push.PushAsync(batch[0]);

        Assert.AreEqual(PushOutcome.Pushed, outcome);
        var row = new LocalFileRepository(_db.Connection).GetByPath("a.txt");
        Assert.AreEqual(serverId, row?.ServerFileId);
        Assert.AreEqual("HASH123", row?.Hash);
        Assert.AreEqual(0, queue.CountPending());
    }

    [TestMethod]
    public async Task Delete_MissingServerId_StaysQueued()
    {
        var (queue, push, _) = Create(_ => Json(HttpStatusCode.NoContent, new { }));
        SeedFile(_db, "x.txt", null);
        queue.Enqueue(PendingOpType.DeleteFile, "x.txt");
        var batch = queue.DequeueEligibleBatch(10);

        var outcome = await push.PushAsync(batch[0]);

        Assert.AreEqual(PushOutcome.TransientFailure, outcome);
        Assert.AreEqual(1, queue.CountPending());
    }

    [TestMethod]
    public async Task Status409_KeepsOp_DoesNotDropData()
    {
        var (queue, push, _) = Create(_ => Json(HttpStatusCode.Conflict, new { code = "FILE_NAME_CONFLICT" }));
        SeedFile(_db, "clash.txt", Guid.NewGuid());
        File.WriteAllText(Path.Combine(_root, "clash.txt"), "local data");
        queue.Enqueue(PendingOpType.CreateFile, "clash.txt");
        var batch = queue.DequeueEligibleBatch(10);

        var outcome = await push.PushAsync(batch[0]);

        Assert.AreEqual(PushOutcome.Conflict, outcome);
        Assert.AreEqual(1, queue.CountPending());
        Assert.IsNotNull(new LocalFileRepository(_db.Connection).GetByPath("clash.txt"));
    }

    [TestMethod]
    public async Task Status403_StopsRun_SurfacesForbidden()
    {
        var (queue, push, _) = Create(_ => new HttpResponseMessage(HttpStatusCode.Forbidden));
        SeedFolder(_db, "F", null);
        queue.Enqueue(PendingOpType.CreateFolder, "F");
        var batch = queue.DequeueEligibleBatch(10);

        var outcome = await push.PushAsync(batch[0]);

        Assert.AreEqual(PushOutcome.Forbidden, outcome);
        Assert.AreEqual(1, queue.CountPending());
    }

    [TestMethod]
    public async Task Status404_OnDelete_MeansAlreadyGone_CompletesOp()
    {
        var (queue, push, _) = Create(_ => new HttpResponseMessage(HttpStatusCode.NotFound));
        var folderId = Guid.NewGuid();
        SeedFolder(_db, "G", folderId);
        queue.Enqueue(PendingOpType.DeleteFolder, "G", serverId: folderId);
        var copy = queue.DequeueEligibleBatch(10)[0];

        var outcome = await push.PushAsync(copy);

        Assert.AreEqual(PushOutcome.Pushed, outcome);
        Assert.AreEqual(0, queue.CountPending());
    }

    [TestMethod]
    public async Task Status404_OnCreate_MarksNeedsReconcile_KeepsOp()
    {
        var (queue, push, _) = Create(_ => new HttpResponseMessage(HttpStatusCode.NotFound));
        SeedFolder(_db, "H", null);
        queue.Enqueue(PendingOpType.CreateFolder, "H");
        var op = queue.DequeueEligibleBatch(10)[0];

        var outcome = await push.PushAsync(op);

        Assert.AreEqual(PushOutcome.NeedsReconcile, outcome);
        Assert.AreEqual(1, queue.CountPending());
    }

    [TestMethod]
    public async Task Status500_AndTimeout_KeepOpQueued()
    {
        var calls = 0;
        var (queue, push, _) = Create(_ =>
        {
            calls++;
            return calls == 1
                ? new HttpResponseMessage(HttpStatusCode.InternalServerError)
                : throw new HttpRequestException("simulated timeout");
        });
        SeedFolder(_db, "H", null);
        queue.Enqueue(PendingOpType.CreateFolder, "H");
        var batch = queue.DequeueEligibleBatch(10);

        Assert.AreEqual(PushOutcome.TransientFailure, await push.PushAsync(batch[0]));
        Assert.AreEqual(PushOutcome.TransientFailure, await push.PushAsync(batch[0]));
        Assert.AreEqual(1, queue.CountPending());
        Assert.AreEqual(2, calls);
    }

    [TestMethod]
    public async Task Move_UpdatesLocalRow_PreservesServerId()
    {
        var serverId = Guid.NewGuid();
        var (queue, push, _) = Create(_ => Json(HttpStatusCode.OK, new { ok = true }));
        SeedFile(_db, "old.txt", serverId);
        queue.Enqueue(PendingOpType.MoveFile, "old.txt", newRelPath: "new.txt", serverId: serverId);
        var batch = queue.DequeueEligibleBatch(10);

        var outcome = await push.PushAsync(batch[0]);

        Assert.AreEqual(PushOutcome.Pushed, outcome);
        Assert.IsNull(new LocalFileRepository(_db.Connection).GetByPath("old.txt"));
        var moved = new LocalFileRepository(_db.Connection).GetByPath("new.txt");
        Assert.AreEqual(serverId, moved?.ServerFileId);
        Assert.AreEqual(0, queue.CountPending());
    }

    [TestMethod]
    public async Task Coordinator_CoalescesConcurrentRuns()
    {
        var gate = new TaskCompletionSource<bool>();
        var entered = 0;
        var folderId = Guid.NewGuid();
        var handler = new StubHandler(async _ =>
        {
            Interlocked.Increment(ref entered);
            await gate.Task;
            return Json(HttpStatusCode.Created, new { id = folderId, name = "C" });
        });
        var auth = new DesktopAuthService("http://localhost:5000", Path.Combine(_dir, "auth"), handler);
        var queue = new PendingQueueService(_db);
        var push = new PushPipeline(_db, queue, auth, _root);
        SeedFolder(_db, "C", null);
        queue.Enqueue(PendingOpType.CreateFolder, "C");
        var coordinator = new SyncCoordinator(_db, queue, push, new ConflictHandler(_db, _root));

        var t1 = coordinator.RunOnceAsync();
        await Task.Delay(200);
        var t2 = coordinator.RunOnceAsync();
        gate.SetResult(true);
        var r1 = await t1;
        var r2 = await t2;

        Assert.AreEqual(1, r1.Attempted + r2.Attempted);
        Assert.AreEqual(0, r2.Attempted);
        Assert.IsTrue(coordinator.HasPendingSignal);
        Assert.AreEqual(1, entered);
    }

    [TestMethod]
    public async Task Coordinator_SyncNowTimesThree_ExactlyOneRun()
    {
        var runs = 0;
        var (queue, push, _) = Create(_ =>
        {
            Interlocked.Increment(ref runs);
            return Json(HttpStatusCode.Created, new { id = Guid.NewGuid(), name = "S" });
        });
        SeedFolder(_db, "S", null);
        queue.Enqueue(PendingOpType.CreateFolder, "S");
        var coordinator = new SyncCoordinator(_db, queue, push, new ConflictHandler(_db, _root));

        coordinator.RequestSync();
        coordinator.RequestSync();
        coordinator.RequestSync();
        var result = await coordinator.RunOnceAsync();

        Assert.AreEqual(1, result.Attempted);
        Assert.AreEqual(1, result.Pushed);
        Assert.AreEqual(1, runs);
        Assert.AreEqual(0, queue.CountPending());
    }

    [TestMethod]
    public async Task Push_401RefreshThenSuccess_CompletesWithSameOperationId()
    {
        var seenOpIds = new List<string?>();
        var calls = 0;
        var serverId = Guid.NewGuid();
        var handler = new StubHandler(req =>
        {
            calls++;
            var path = req.RequestUri!.AbsolutePath;
            if (path.EndsWith("/api/auth/refresh"))
                return Json(HttpStatusCode.OK, new { accessToken = "jwt-new" });
            if (calls == 1)
                return new HttpResponseMessage(HttpStatusCode.Unauthorized);
            seenOpIds.Add(req.Headers.TryGetValues("X-Operation-Id", out var v) ? string.Join(",", v) : null);
            return Json(HttpStatusCode.Created, new { id = serverId, name = "R" });
        });
        var auth = new DesktopAuthService("http://localhost:5000", Path.Combine(_dir, "auth"), handler);
        var queue = new PendingQueueService(_db);
        var push = new PushPipeline(_db, queue, auth, _root);
        SeedFolder(_db, "R", null);
        var opId = queue.Enqueue(PendingOpType.CreateFolder, "R");
        var batch = queue.DequeueEligibleBatch(10);

        var outcome = await push.PushAsync(batch[0]);

        Assert.AreEqual(PushOutcome.Pushed, outcome);
        Assert.AreEqual(1, seenOpIds.Count);
        Assert.AreEqual(opId.ToString(), seenOpIds[0]);
        Assert.AreEqual(0, queue.CountPending());
    }

    [TestMethod]
    public async Task Push_429_KeepsOp_AndBackoffScheduleGrows()
    {
        var (queue, push, _) = Create(_ => new HttpResponseMessage((HttpStatusCode)429));
        SeedFolder(_db, "T", null);
        queue.Enqueue(PendingOpType.CreateFolder, "T");
        var batch = queue.DequeueEligibleBatch(10);

        var outcome = await push.PushAsync(batch[0]);

        Assert.AreEqual(PushOutcome.TransientFailure, outcome);
        Assert.AreEqual(1, queue.CountPending());
        Assert.AreEqual(TimeSpan.FromSeconds(2), SyncCoordinator.ComputeBackoffDelay(1));
        Assert.AreEqual(TimeSpan.FromSeconds(4), SyncCoordinator.ComputeBackoffDelay(2));
        Assert.AreEqual(TimeSpan.FromSeconds(30), SyncCoordinator.ComputeBackoffDelay(10));
    }

    [TestMethod]
    public async Task Push_CanceledTimeout_KeepsOpQueued()
    {
        var (queue, push, _) = Create(_ => throw new TaskCanceledException("simulated timeout"));
        SeedFolder(_db, "Z", null);
        queue.Enqueue(PendingOpType.CreateFolder, "Z");
        var batch = queue.DequeueEligibleBatch(10);

        var outcome = await push.PushAsync(batch[0]);

        Assert.AreEqual(PushOutcome.TransientFailure, outcome);
        Assert.AreEqual(1, queue.CountPending());
    }

    [TestMethod]
    public async Task Push_SendsDeviceIdHeader()
    {
        HttpRequestMessage? seen = null;
        var serverId = Guid.NewGuid();
        var deviceId = Guid.NewGuid();
        var authDir = Path.Combine(_dir, "auth-dev");
        Directory.CreateDirectory(authDir);
        File.WriteAllText(Path.Combine(authDir, "device.json"),
            JsonSerializer.Serialize(new { deviceId = deviceId.ToString() }));
        var handler = new StubHandler(req =>
        {
            seen = req;
            return Json(HttpStatusCode.Created, new { id = serverId, name = "D" });
        });
        var auth = new DesktopAuthService("http://localhost:5000", authDir, handler);
        var queue = new PendingQueueService(_db);
        var push = new PushPipeline(_db, queue, auth, _root);
        SeedFolder(_db, "D", null);
        queue.Enqueue(PendingOpType.CreateFolder, "D");
        var batch = queue.DequeueEligibleBatch(10);

        var outcome = await push.PushAsync(batch[0]);

        Assert.AreEqual(PushOutcome.Pushed, outcome);
        Assert.IsNotNull(seen);
        Assert.IsTrue(seen.Headers.Contains("X-Device-Id"));
        Assert.AreEqual(deviceId.ToString(), string.Join(",", seen.Headers.GetValues("X-Device-Id")));
    }

    [TestMethod]
    public async Task Coordinator_BoundedBatch_RespectsSize()
    {
        var (queue, push, _) = Create(_ => Json(HttpStatusCode.Created, new { id = Guid.NewGuid(), name = "B" }));
        queue.Enqueue(PendingOpType.CreateFolder, "BB1");
        queue.Enqueue(PendingOpType.CreateFolder, "BB2");
        var coordinator = new SyncCoordinator(_db, queue, push, new ConflictHandler(_db, _root), batchSize: 1);

        var result = await coordinator.RunOnceAsync();

        Assert.AreEqual(1, result.Attempted);
        Assert.AreEqual(1, result.Pushed);
        Assert.AreEqual(1, queue.CountPending());
    }

    [TestMethod]
    public async Task Coordinator_BackoffSleepsBetweenTransientFailures()
    {
        var sleeps = new List<TimeSpan>();
        var (queue, push, _) = Create(_ => new HttpResponseMessage(HttpStatusCode.InternalServerError));
        SeedFolder(_db, "BO", null);
        queue.Enqueue(PendingOpType.CreateFolder, "BO1");
        queue.Enqueue(PendingOpType.CreateFolder, "BO2");
        var coordinator = new SyncCoordinator(_db, queue, push, new ConflictHandler(_db, _root), batchSize: 10,
            sleeper: (delay, _) => { sleeps.Add(delay); return Task.CompletedTask; });

        var result = await coordinator.RunOnceAsync();

        Assert.AreEqual(2, result.Attempted);
        Assert.AreEqual(1, sleeps.Count);
        Assert.AreEqual(TimeSpan.FromSeconds(2), sleeps[0]);
        Assert.AreEqual(2, queue.CountPending());
    }

    [TestMethod]
    public async Task OfflineOp_ReconnectPushesWithSameOperationId()
    {
        var online = false;
        var seenOpIds = new List<string>();
        var serverId = Guid.NewGuid();
        var (queue, push, _) = Create(req =>
        {
            if (!online)
                throw new HttpRequestException("simulated offline");
            seenOpIds.Add(req.Headers.TryGetValues("X-Operation-Id", out var v) ? string.Join(",", v) : string.Empty);
            return Json(HttpStatusCode.Created, new { id = serverId, name = "Offline" });
        });
        File.WriteAllText(Path.Combine(_root, "offline.txt"), "offline-bytes");
        SeedFile(_db, "offline.txt", null);
        var opId = queue.Enqueue(PendingOpType.CreateFile, "offline.txt");
        var coordinator = new SyncCoordinator(_db, queue, push, new ConflictHandler(_db, _root));

        var offlineResult = await coordinator.RunOnceAsync();

        online = true;
        var onlineResult = await coordinator.RunOnceAsync();

        Assert.AreEqual(1, offlineResult.Attempted);
        Assert.AreEqual(0, offlineResult.Pushed);
        Assert.AreEqual(1, queue.CountPending() + onlineResult.Pushed,
            "Offline run must retain the op; reconnect run pushes it.");
        Assert.AreEqual(1, onlineResult.Pushed);
        Assert.AreEqual(0, queue.CountPending());
        Assert.AreEqual(1, seenOpIds.Count);
        Assert.AreEqual(opId.ToString(), seenOpIds[0],
            "Reconnect must reuse the original OperationId so server replay stays idempotent.");
        Assert.AreEqual(serverId, new LocalFileRepository(_db.Connection).GetByPath("offline.txt")?.ServerFileId);
    }
}

