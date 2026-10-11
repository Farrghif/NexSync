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
public sealed class PullPipelineTests
{
    private string _dir = null!;
    private string _root = null!;
    private string DbPath => Path.Combine(_dir, ".nexsync", "local.db");
    private LocalDb _db = null!;

    [TestInitialize]
    public void Setup()
    {
        _dir = Path.Combine(Path.GetTempPath(), "nexsync-pull-test", Guid.NewGuid().ToString("N"));
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
        public StubHandler(Func<HttpRequestMessage, Task<HttpResponseMessage>> async) : this(req => async(req).GetAwaiter().GetResult())
        {
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
            => Task.FromResult(respond(request));
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

    private (PullPipeline pull, PendingQueueService queue) Create(Func<HttpRequestMessage, HttpResponseMessage> respond)
    {
        var handler = new StubHandler(respond);
        var auth = new DesktopAuthService("http://localhost:5000", Path.Combine(_dir, "auth"), handler);
        var queue = new PendingQueueService(_db);
        return (new PullPipeline(_db, queue, auth, _root), queue);
    }

    private static void SeedFolder(LocalDb db, string rel, Guid? serverId)
        => new LocalFolderRepository(db.Connection).Upsert(
            new LocalFolderRow(rel, serverId, null, 0, DateTime.UtcNow));

    private static void SeedFile(LocalDb db, string rel, Guid? serverId, string hash = "abc")
        => new LocalFileRepository(db.Connection).Upsert(
            new LocalFileRow(rel, serverId, null, hash, 10, 0, DateTime.UtcNow));

    private static string Sha256Of(byte[] bytes)
    {
        using var sha = System.Security.Cryptography.SHA256.Create();
        return Convert.ToHexString(sha.ComputeHash(bytes));
    }

    [TestMethod]
    public async Task MultiBatchPull_AppliesInOrder_AdvancesCursorToEnd()
    {
        var folderId = Guid.NewGuid();
        var fileId = Guid.NewGuid();
        var payload = Encoding.UTF8.GetBytes("remote content");
        var (pull, _) = Create(req =>
        {
            var url = req.RequestUri!.ToString();
            if (url.Contains("/api/files/") && url.Contains("/download"))
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new ByteArrayContent(payload)
                };
            var query = System.Web.HttpUtility.ParseQueryString(req.RequestUri!.Query);
            var since = long.Parse(query["since"]!);
            var changes = new List<object>();
            if (since < 1) changes.Add(Change(1, "Folder", folderId, "Created", "Docs", null));
            if (since < 2) changes.Add(Change(2, "File", fileId, "Created", "readme.txt", folderId, Sha256Of(payload), payload.Length, "text/plain"));
            return Json(HttpStatusCode.OK, new
            {
                changes,
                nextCursor = changes.Count == 0 ? since : 2L,
                hasMore = false,
                highWaterCursor = 2L
            });
        });

        var outcome = await pull.PullAndApplyAsync(until: 2);

        Assert.AreEqual(PullApplyOutcome.Applied, outcome);
        Assert.AreEqual(2L, pull.GetCursor());
        Assert.IsTrue(Directory.Exists(Path.Combine(_root, "Docs")));
        Assert.AreEqual("remote content", File.ReadAllText(Path.Combine(_root, "Docs", "readme.txt")));
        var row = new LocalFileRepository(_db.Connection).GetByServerId(fileId);
        Assert.AreEqual("Docs/readme.txt", row?.RelativePath);
        Assert.AreEqual(2L, row?.LastAppliedSequence);
    }

    [TestMethod]
    public async Task Cursor_PersistedOnlyAfterFullBatch()
    {
        var fileId = Guid.NewGuid();
        var calls = 0;
        var (pull, _) = Create(req =>
        {
            var url = req.RequestUri!.ToString();
            if (url.Contains("/download"))
                return new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(new byte[] { 9 }) };
            calls++;
            if (calls == 1)
                return Json(HttpStatusCode.OK, new
                {
                    changes = new[] { Change(1, "Folder", Guid.NewGuid(), "Created", "A", null) },
                    nextCursor = 1L,
                    hasMore = true,
                    highWaterCursor = 2L
                });
            return Json(HttpStatusCode.OK, new
            {
                changes = new[] { Change(2, "File", fileId, "Created", "b.txt", null, Sha256Of(new byte[] { 9 }), 1L, "application/octet-stream") },
                nextCursor = 2L,
                hasMore = false,
                highWaterCursor = 2L
            });
        });

        var outcome = await pull.PullAndApplyAsync(until: 2);

        Assert.AreEqual(PullApplyOutcome.Applied, outcome);
        Assert.AreEqual(2L, pull.GetCursor());
        Assert.IsTrue(File.Exists(Path.Combine(_root, "b.txt")));
    }

    [TestMethod]
    public async Task HashMismatch_DiscardsTemp_KeepsOriginal_AndStopsRun()
    {
        var fileId = Guid.NewGuid();
        SeedFile(_db, "keep.txt", fileId, hash: "ORIGINAL");
        File.WriteAllText(Path.Combine(_root, "keep.txt"), "original-bytes");
        var (pull, _) = Create(req =>
        {
            if (req.RequestUri!.ToString().Contains("/download"))
                return new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(Encoding.UTF8.GetBytes("tampered")) };
            return Json(HttpStatusCode.OK, new
            {
                changes = new[] { Change(5, "File", fileId, "Modified", "keep.txt", null, Sha256Of(Encoding.UTF8.GetBytes("expected")), 8L, "text/plain") },
                nextCursor = 5L,
                hasMore = false,
                highWaterCursor = 5L
            });
        });

        var outcome = await pull.PullAndApplyAsync(until: 5);

        Assert.AreEqual(PullApplyOutcome.TransientFailure, outcome);
        Assert.AreEqual("original-bytes", File.ReadAllText(Path.Combine(_root, "keep.txt")));
        Assert.AreEqual(0, Directory.GetFiles(_root, "*.nexsync-temp-*").Length);
        Assert.AreEqual(0L, pull.GetCursor());
    }

    [TestMethod]
    public async Task Rename_UpdatesExistingRowByServerId_NoDuplicate()
    {
        var folderId = Guid.NewGuid();
        SeedFolder(_db, "Old", folderId);
        Directory.CreateDirectory(Path.Combine(_root, "Old"));
        var (pull, _) = Create(_ => Json(HttpStatusCode.OK, new
        {
            changes = new[] { Change(3, "Folder", folderId, "Renamed", "New", null) },
            nextCursor = 3L,
            hasMore = false,
            highWaterCursor = 3L
        }));

        var outcome = await pull.PullAndApplyAsync(until: 3);

        Assert.AreEqual(PullApplyOutcome.Applied, outcome);
        Assert.IsTrue(Directory.Exists(Path.Combine(_root, "New")));
        Assert.IsFalse(Directory.Exists(Path.Combine(_root, "Old")));
        var moved = new LocalFolderRepository(_db.Connection).GetByServerId(folderId);
        Assert.AreEqual("New", moved?.RelativePath);
        Assert.AreEqual(folderId, moved?.ServerFolderId);
    }

    [TestMethod]
    public async Task UnknownParent_ReturnsTransient_CursorNotAdvanced()
    {
        var (pull, _) = Create(_ => Json(HttpStatusCode.OK, new
        {
            changes = new[] { Change(7, "File", Guid.NewGuid(), "Created", "orphan.txt", Guid.NewGuid(), Sha256Of(new byte[] { 1 }), 1L, "application/octet-stream") },
            nextCursor = 7L,
            hasMore = false,
            highWaterCursor = 7L
        }));

        var outcome = await pull.PullAndApplyAsync(until: 7);

        Assert.AreEqual(PullApplyOutcome.TransientFailure, outcome);
        Assert.AreEqual(0L, pull.GetCursor());
        Assert.IsFalse(File.Exists(Path.Combine(_root, "orphan.txt")));
    }

    [TestMethod]
    public async Task Delete_RemovesFileAndRow()
    {
        var fileId = Guid.NewGuid();
        SeedFile(_db, "bye.txt", fileId);
        File.WriteAllText(Path.Combine(_root, "bye.txt"), "bye");
        var (pull, _) = Create(_ => Json(HttpStatusCode.OK, new
        {
            changes = new[] { Change(9, "File", fileId, "Deleted", "bye.txt", null) },
            nextCursor = 9L,
            hasMore = false,
            highWaterCursor = 9L
        }));

        var outcome = await pull.PullAndApplyAsync(until: 9);

        Assert.AreEqual(PullApplyOutcome.Applied, outcome);
        Assert.IsFalse(File.Exists(Path.Combine(_root, "bye.txt")));
        Assert.IsNull(new LocalFileRepository(_db.Connection).GetByServerId(fileId));
        Assert.AreEqual(9L, pull.GetCursor());
    }

    [TestMethod]
    public async Task OwnConfirmedChange_IsSkipped_UnknownOutcome_IsApplied()
    {
        var ownDevice = Guid.NewGuid();
        var fileId = Guid.NewGuid();
        var payload = Encoding.UTF8.GetBytes("server-wins-content");
        var downloadCalls = 0;
        var handler = new StubHandler(req =>
        {
            if (req.RequestUri!.ToString().Contains("/download"))
            {
                downloadCalls++;
                return new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(payload) };
            }
            return Json(HttpStatusCode.OK, new
            {
                changes = new[] { Change(11, "File", fileId, "Modified", "mine.txt", null, Sha256Of(payload), payload.Length, "text/plain", ownDevice) },
                nextCursor = 11L,
                hasMore = false,
                highWaterCursor = 11L
            });
        });
        var authDir = Path.Combine(_dir, "auth-own");
        Directory.CreateDirectory(authDir);
        File.WriteAllText(Path.Combine(authDir, "device.json"),
            System.Text.Json.JsonSerializer.Serialize(new { deviceId = ownDevice.ToString() }));
        var auth = new DesktopAuthService("http://localhost:5000", authDir, handler);
        var queue = new PendingQueueService(_db);
        var pull = new PullPipeline(_db, queue, auth, _root);

        SeedFile(_db, "mine.txt", fileId, hash: Sha256Of(payload));
        File.WriteAllText(Path.Combine(_root, "mine.txt"), "server-wins-content");
        var outcome = await pull.PullAndApplyAsync(until: 11);

        Assert.AreEqual(PullApplyOutcome.Applied, outcome);
        Assert.AreEqual(0, downloadCalls);
        Assert.AreEqual(11L, pull.GetCursor());
    }

    [TestMethod]
    public async Task AuthStates_ProduceCorrectOutcomes()
    {
        var (forbidden, _) = Create(_ => new HttpResponseMessage(HttpStatusCode.Forbidden));
        Assert.AreEqual(PullApplyOutcome.Forbidden, await forbidden.PullAndApplyAsync(until: 10));

        var (unauth, _) = Create(_ => new HttpResponseMessage(HttpStatusCode.Unauthorized));
        Assert.AreEqual(PullApplyOutcome.AuthFailed, await unauth.PullAndApplyAsync(until: 10));

        var (err, _) = Create(_ => new HttpResponseMessage(HttpStatusCode.InternalServerError));
        Assert.AreEqual(PullApplyOutcome.TransientFailure, await err.PullAndApplyAsync(until: 10));
        Assert.AreEqual(0L, err.GetCursor());
    }
}
