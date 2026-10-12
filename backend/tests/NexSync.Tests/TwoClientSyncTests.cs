using System.Net;
using System.Net.Http.Json;
using System.Text;

namespace NexSync.Tests;

[TestClass]
public sealed class TwoClientSyncTests
{
    private readonly NexSyncApiFactory _factory = new();
    private ApiClient _desktop = null!;
    private ApiClient _web = null!;
    private Guid _desktopId;
    private Guid _webId;

    [TestInitialize]
    public async Task InitializeAsync()
    {
        await _factory.InitializeAsync();
        _desktop = new ApiClient(_factory.CreateClient());
        _web = new ApiClient(_factory.CreateClient());
        await _desktop.RegisterAsync("e2e@nexsync.dev", "password123", "E2E User");
        await _web.LoginAsync("e2e@nexsync.dev", "password123");
        _desktopId = await RegisterDeviceAsync(_desktop, "E2E Desktop", "Windows");
        _webId = await RegisterDeviceAsync(_web, "Chrome - Windows", "Web");
        _desktop.DeviceId = _desktopId;
        _web.DeviceId = _webId;
    }

    [TestCleanup]
    public async Task DisposeAsync() => await _factory.DisposeAsync();

    private static async Task<Guid> RegisterDeviceAsync(ApiClient client, string name, string platform)
    {
        var req = new HttpRequestMessage(HttpMethod.Post, "/api/devices")
        {
            Content = JsonContent.Create(new { name, platform })
        };
        req.Headers.Add("X-Operation-Id", Guid.NewGuid().ToString());
        var res = await client.SendRawAsync(req);
        Assert.AreEqual(HttpStatusCode.Created, res.StatusCode);
        var doc = await ApiClient.ParseAsync(res);
        return doc!.RootElement.GetProperty("deviceId").GetGuid();
    }

    private static async Task<Guid> CreateFolderAsync(ApiClient client, string name, Guid? parentId = null)
    {
        var req = new HttpRequestMessage(HttpMethod.Post, "/api/folders")
        {
            Content = JsonContent.Create(new { name, parentFolderId = parentId })
        };
        req.Headers.Add("X-Operation-Id", Guid.NewGuid().ToString());
        var res = await client.SendRawAsync(req);
        Assert.AreEqual(HttpStatusCode.Created, res.StatusCode);
        var doc = await ApiClient.ParseAsync(res);
        return doc!.RootElement.GetProperty("id").GetGuid();
    }

    private static async Task<(Guid Id, string Hash)> UploadAsync(ApiClient client, byte[] bytes, string name, Guid? folderId = null)
    {
        var (res, doc) = await client.UploadAsync(bytes, name, "text/plain", folderId);
        Assert.AreEqual(HttpStatusCode.Created, res.StatusCode);
        return (doc!.RootElement.GetProperty("id").GetGuid(),
            doc.RootElement.GetProperty("hash").GetString()!);
    }

    private static async Task<System.Text.Json.JsonElement> PullAsync(ApiClient client, long since, long until)
    {
        var (res, doc) = await client.GetAsync($"/api/sync/pull?since={since}&until={until}&limit=500");
        Assert.AreEqual(HttpStatusCode.OK, res.StatusCode);
        return doc!.RootElement;
    }

    [TestMethod]
    public async Task DesktopUpload_WebReadsSameBytes_OriginAttributed()
    {
        var payload = Encoding.UTF8.GetBytes("desktop-original-bytes");
        var (id, _) = await UploadAsync(_desktop, payload, "shared.txt");

        var down = await _web.DownloadRawAsync(id);
        Assert.AreEqual(HttpStatusCode.OK, down.StatusCode);
        CollectionAssert.AreEqual(payload, await down.Content.ReadAsByteArrayAsync());

        var pull = await PullAsync(_web, 0, 100);
        var change = pull.GetProperty("changes").EnumerateArray()
            .Single(c => c.GetProperty("entityId").GetGuid() == id);
        Assert.AreEqual("Created", change.GetProperty("operation").GetString());
        Assert.AreEqual(_desktopId, change.GetProperty("originDeviceId").GetGuid());
    }

    [TestMethod]
    public async Task WebRenameMoveDelete_DesktopPullSeesSnapshotsWithWebOrigin()
    {
        var payload = Encoding.UTF8.GetBytes("roundtrip-bytes");
        var (id, _) = await UploadAsync(_desktop, payload, "doc.txt");

        var (renRes, _) = await _web.PutJsonAsync($"/api/files/{id}", new { name = "renamed.txt", folderId = (Guid?)null });
        Assert.AreEqual(HttpStatusCode.OK, renRes.StatusCode);
        var folderId = await CreateFolderAsync(_web, "Archive");
        var (movRes, _) = await _web.PutJsonAsync($"/api/files/{id}", new { name = "renamed.txt", folderId });
        Assert.AreEqual(HttpStatusCode.OK, movRes.StatusCode);

        var pull = await PullAsync(_desktop, 0, 100);
        var ops = pull.GetProperty("changes").EnumerateArray()
            .Where(c => c.GetProperty("entityId").GetGuid() == id)
            .Select(c => c.GetProperty("operation").GetString()).ToList();
        CollectionAssert.AreEquivalent(new[] { "Created", "Renamed", "Moved" }, ops);
        foreach (var c in pull.GetProperty("changes").EnumerateArray()
            .Where(c => c.GetProperty("entityId").GetGuid() == id && c.GetProperty("operation").GetString() != "Created"))
            Assert.AreEqual(_webId, c.GetProperty("originDeviceId").GetGuid());

        var del = await _web.DeleteAsync($"/api/files/{id}");
        Assert.AreEqual(HttpStatusCode.NoContent, del.StatusCode);
        var cursor = pull.GetProperty("nextCursor").GetInt64();
        var pull2 = await PullAsync(_desktop, cursor, 100);
        var tomb = pull2.GetProperty("changes").EnumerateArray()
            .Single(c => c.GetProperty("entityId").GetGuid() == id && c.GetProperty("operation").GetString() == "Deleted");
        Assert.AreEqual(_webId, tomb.GetProperty("originDeviceId").GetGuid());
        Assert.IsTrue(tomb.GetProperty("hash").ValueKind == System.Text.Json.JsonValueKind.Null);
    }

    [TestMethod]
    public async Task DesktopFolderTree_WebListsSameStructure()
    {
        var rootId = await CreateFolderAsync(_desktop, "Projects");
        var subId = await CreateFolderAsync(_desktop, "Sub", rootId);
        await UploadAsync(_desktop, Encoding.UTF8.GetBytes("nested"), "nested.txt", subId);

        var (res, doc) = await _web.GetAsync($"/api/folders?parentId={rootId}&page=1&pageSize=50");
        Assert.AreEqual(HttpStatusCode.OK, res.StatusCode);
        Assert.IsTrue(doc!.RootElement.GetProperty("folders").EnumerateArray()
            .Any(f => f.GetProperty("id").GetGuid() == subId));
    }

    [TestMethod]
    public async Task DesktopLifecycle_CreateEditRenameMoveDelete_WebPullSeesAllWithDesktopOrigin()
    {
        var folderA = await CreateFolderAsync(_desktop, "LifeA");
        var folderB = await CreateFolderAsync(_desktop, "LifeB");
        var (id, _) = await UploadAsync(_desktop, Encoding.UTF8.GetBytes("v1"), "life.txt", folderA);

        var (editRes, _) = await _desktop.PutJsonAsync($"/api/files/{id}", new { name = "life.txt", folderId = (Guid?)folderA });
        Assert.AreEqual(HttpStatusCode.OK, editRes.StatusCode);
        var (renRes, _) = await _desktop.PutJsonAsync($"/api/files/{id}", new { name = "life2.txt", folderId = (Guid?)folderA });
        Assert.AreEqual(HttpStatusCode.OK, renRes.StatusCode);
        var (movRes, _) = await _desktop.PutJsonAsync($"/api/files/{id}", new { name = "life2.txt", folderId = (Guid?)folderB });
        Assert.AreEqual(HttpStatusCode.OK, movRes.StatusCode);
        var delRes = await _desktop.DeleteAsync($"/api/files/{id}");
        Assert.AreEqual(HttpStatusCode.NoContent, delRes.StatusCode);

        var pull = await PullAsync(_web, 0, 100);
        var ops = pull.GetProperty("changes").EnumerateArray()
            .Where(c => c.GetProperty("entityId").GetGuid() == id)
            .Select(c => c.GetProperty("operation").GetString()).ToList();
        CollectionAssert.AreEquivalent(new[] { "Created", "Modified", "Renamed", "Moved", "Deleted" }, ops);
        foreach (var c in pull.GetProperty("changes").EnumerateArray()
            .Where(c => c.GetProperty("entityId").GetGuid() == id))
            Assert.AreEqual(_desktopId, c.GetProperty("originDeviceId").GetGuid());
    }

    [TestMethod]
    public async Task BurstWrites_PagedPull_ConvergesExactlyOnce()
    {
        for (var i = 0; i < 10; i++)
            await CreateFolderAsync(_desktop, $"Burst{i}");

        var seen = new List<long>();
        long cursor = 0;
        bool hasMore = true;
        while (hasMore)
        {
            var (res, doc) = await _web.GetAsync($"/api/sync/pull?since={cursor}&until=50&limit=3");
            Assert.AreEqual(HttpStatusCode.OK, res.StatusCode);
            var root = doc!.RootElement;
            foreach (var c in root.GetProperty("changes").EnumerateArray())
                seen.Add(c.GetProperty("sequence").GetInt64());
            cursor = root.GetProperty("nextCursor").GetInt64();
            hasMore = root.GetProperty("hasMore").GetBoolean();
        }
        CollectionAssert.AreEqual(Enumerable.Range(1, 10).Select(i => (long)i).ToList(), seen);
        Assert.AreEqual(10L, cursor);
    }

    [TestMethod]
    public async Task ManifestSnapshot_ConcurrentWebWrite_PulledExactlyOnceAfterCursor()
    {
        var (mres, mdoc) = await _desktop.GetAsync("/api/sync/manifest");
        Assert.AreEqual(HttpStatusCode.OK, mres.StatusCode);
        var snapshot = mdoc!.RootElement.GetProperty("snapshotCursor").GetInt64();

        await UploadAsync(_web, Encoding.UTF8.GetBytes("concurrent-bytes"), "concurrent.txt");

        var pull = await PullAsync(_desktop, snapshot, snapshot + 10);
        var hits = pull.GetProperty("changes").EnumerateArray()
            .Where(c => c.GetProperty("name").GetString() == "concurrent.txt").ToList();
        Assert.AreEqual(1, hits.Count);
        Assert.AreEqual(_webId, hits[0].GetProperty("originDeviceId").GetGuid());
    }

    [TestMethod]
    public async Task ImportCollision_SecondUpload409_FirstUploadIntact()
    {
        var first = Encoding.UTF8.GetBytes("server-original");
        var (id, _) = await UploadAsync(_desktop, first, "note.txt");

        var (conflictRes, _) = await _web.UploadAsync(Encoding.UTF8.GetBytes("import-collision"), "NOTE.txt", "text/plain");
        Assert.AreEqual(HttpStatusCode.Conflict, conflictRes.StatusCode);

        var down = await _desktop.DownloadRawAsync(id);
        Assert.AreEqual(HttpStatusCode.OK, down.StatusCode);
        CollectionAssert.AreEqual(first, await down.Content.ReadAsByteArrayAsync());
    }

    [TestMethod]
    public async Task TimeoutRetry_SingleEvent_WebSeesOnce()
    {
        var opId = Guid.NewGuid();
        var body = new { name = "RetryMe", parentFolderId = (Guid?)null };
        HttpResponseMessage r1, r2;
        var req1 = new HttpRequestMessage(HttpMethod.Post, "/api/folders") { Content = JsonContent.Create(body) };
        req1.Headers.Add("X-Operation-Id", opId.ToString());
        r1 = await _desktop.SendRawAsync(req1);
        var req2 = new HttpRequestMessage(HttpMethod.Post, "/api/folders") { Content = JsonContent.Create(body) };
        req2.Headers.Add("X-Operation-Id", opId.ToString());
        r2 = await _desktop.SendRawAsync(req2);

        Assert.AreEqual(HttpStatusCode.Created, r1.StatusCode);
        Assert.AreEqual(HttpStatusCode.Created, r2.StatusCode);
        var d1 = await ApiClient.ParseAsync(r1);
        var d2 = await ApiClient.ParseAsync(r2);
        Assert.AreEqual(
            d1!.RootElement.GetProperty("id").GetGuid(),
            d2!.RootElement.GetProperty("id").GetGuid());
        Assert.AreEqual(
            d1.RootElement.GetProperty("name").GetString(),
            d2.RootElement.GetProperty("name").GetString());

        var pull = await PullAsync(_web, 0, 100);
        Assert.AreEqual(1, pull.GetProperty("changes").EnumerateArray()
            .Count(c => c.GetProperty("name").GetString() == "RetryMe"));
    }

    [TestMethod]
    public async Task RevokedDesktop_Blocked403_WebUnaffected()
    {
        var del = await _desktop.DeleteAsync($"/api/devices/{_desktopId}");
        Assert.AreEqual(HttpStatusCode.NoContent, del.StatusCode);

        var (mres, _) = await _desktop.PostJsonAsync("/api/folders", new { name = "Blocked", parentFolderId = (Guid?)null });
        Assert.AreEqual(HttpStatusCode.Forbidden, mres.StatusCode);
        var (pres, _) = await _desktop.GetAsync("/api/sync/pull?since=0&until=10&limit=10");
        Assert.AreEqual(HttpStatusCode.Forbidden, pres.StatusCode);

        var folderId = await CreateFolderAsync(_web, "StillWorking");
        Assert.AreNotEqual(Guid.Empty, folderId);
    }
}
