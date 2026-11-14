using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NexSync.Desktop.Data;
using NexSync.Desktop.Sync;

namespace NexSync.Desktop.Tests;

[TestClass]
public sealed class SyncWiringTests
{
    private string _dir = null!;
    private string _root = null!;
    private string _dataDir = null!;

    [TestInitialize]
    public void Setup()
    {
        _dir = Path.Combine(Path.GetTempPath(), "nexsync-wiring-test", Guid.NewGuid().ToString("N"));
        _root = Path.Combine(_dir, "files");
        _dataDir = Path.Combine(_dir, "data");
        Directory.CreateDirectory(_root);
    }

    [TestCleanup]
    public void Teardown()
    {
        if (Directory.Exists(_dir))
            Directory.Delete(_dir, recursive: true);
    }

    private sealed class StubHandler : HttpMessageHandler
    {
        public readonly List<string> Requests = new();
        private readonly Guid _folderId = Guid.NewGuid();

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var path = request.RequestUri!.AbsolutePath;
            lock (Requests) Requests.Add($"{request.Method} {path}");
            return Task.FromResult(Respond(path, request.Method));
        }

        private HttpResponseMessage Respond(string path, HttpMethod method)
        {
            if (path == "/api/sync/manifest")
                return Json(HttpStatusCode.OK, new { snapshotCursor = 0L, folders = Array.Empty<object>(), files = Array.Empty<object>() });
            if (path == "/api/sync/cursor")
                return Json(HttpStatusCode.OK, new { highWaterCursor = 5L });
            if (path == "/api/sync/pull")
                return Json(HttpStatusCode.OK, new
                {
                    changes = new[]
                    {
                        new
                        {
                            sequence = 1L,
                            entityType = "Folder",
                            entityId = _folderId,
                            operation = "Created",
                            name = "FromWeb",
                            parentFolderId = (Guid?)null,
                            hash = (string?)null,
                            size = (long?)null,
                            contentType = (string?)null,
                            originDeviceId = (Guid?)null
                        }
                    },
                    nextCursor = 5L,
                    hasMore = false,
                    highWaterCursor = 5L
                });
            if (path == "/api/folders" && method == HttpMethod.Post)
                return Json(HttpStatusCode.Created, new { id = _folderId });
            return Json(HttpStatusCode.OK, new { });
        }

        private static HttpResponseMessage Json(HttpStatusCode status, object payload)
        {
            var res = new HttpResponseMessage(status);
            res.Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");
            return res;
        }
    }

    [TestMethod]
    public async Task SyncCycle_PullsRemoteFolderAndPushesQueuedLocalOp()
    {
        var stub = new StubHandler();
        using var services = new DesktopServices("http://localhost:5189", _dataDir, stub);

        // A folder that exists on disk before the engine starts is the same situation
        // as a folder created while the app was closed: the startup reconcile must
        // pick it up and push it (the stub manifest does not know it yet).
        Directory.CreateDirectory(Path.Combine(_root, "FromDesktop"));

        services.State.SetLocalRoot(_root);
        await services.RunSyncCycleAsync();

        Assert.IsTrue(Directory.Exists(Path.Combine(_root, "FromWeb")),
            "A folder created on the server must be pulled down to the local root.");
        lock (stub.Requests)
            CollectionAssert.Contains(stub.Requests, "POST /api/folders",
                "A queued local folder must be pushed to the server.");
    }

    [TestMethod]
    public async Task SyncCycle_WithoutLocalRoot_DoesNothing()
    {
        var stub = new StubHandler();
        using var services = new DesktopServices("http://localhost:5189", _dataDir, stub);

        await services.RunSyncCycleAsync();

        lock (stub.Requests)
            Assert.AreEqual(0, stub.Requests.Count, "No sync traffic before a folder is configured.");
    }

    [TestMethod]
    public async Task BackgroundLoop_IdlesWhileSignedOut()
    {
        var stub = new StubHandler();
        using var services = new DesktopServices("http://localhost:5189", _dataDir, stub);

        // SetLocalRoot starts the background loop, but nobody has signed in — the
        // same state the app is in after "Sign out" (which now keeps the folder).
        services.State.SetLocalRoot(_root);
        await Task.Delay(1500);

        lock (stub.Requests)
            Assert.AreEqual(0, stub.Requests.Count,
                "The background loop must not call the server without a session.");
    }
}
