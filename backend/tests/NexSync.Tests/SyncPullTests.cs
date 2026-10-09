using System.Net;
using System.Net.Http.Json;
using Microsoft.Extensions.DependencyInjection;

namespace NexSync.Tests;

[TestClass]
public sealed class SyncPullTests
{
    private readonly NexSyncApiFactory _factory = new();
    private ApiClient _alice = null!;
    private ApiClient _bob = null!;

    [TestInitialize]
    public async Task InitializeAsync()
    {
        await _factory.InitializeAsync();
        _alice = new ApiClient(_factory.CreateClient());
        _bob = new ApiClient(_factory.CreateClient());
        await _alice.RegisterAsync("sync-alice@nexsync.dev", "password123", "Alice");
        await _bob.RegisterAsync("sync-bob@nexsync.dev", "password123", "Bob");
        _alice.DeviceId = await RegisterDeviceAsync(_alice, "Alice Laptop");
        _bob.DeviceId = await RegisterDeviceAsync(_bob, "Bob Phone");
    }

    [TestCleanup]
    public async Task DisposeAsync() => await _factory.DisposeAsync();

    private static async Task<Guid> RegisterDeviceAsync(ApiClient client, string name)
    {
        var req = new HttpRequestMessage(HttpMethod.Post, "/api/devices")
        {
            Content = JsonContent.Create(new { name, platform = "Windows" })
        };
        req.Headers.Add("X-Operation-Id", Guid.NewGuid().ToString());
        var res = await client.SendRawAsync(req);
        Assert.AreEqual(HttpStatusCode.Created, res.StatusCode);
        var doc = await ApiClient.ParseAsync(res);
        return doc!.RootElement.GetProperty("deviceId").GetGuid();
    }

    [TestMethod]
    public async Task Cursor_ReflectsHighestCommittedSequence()
    {
        var (c0, _) = await _alice.GetAsync("/api/sync/cursor");
        Assert.AreEqual(HttpStatusCode.OK, c0.StatusCode);

        await _alice.PostJsonAsync("/api/folders", new { name = "A", parentFolderId = (Guid?)null });
        await _alice.PostJsonAsync("/api/folders", new { name = "B", parentFolderId = (Guid?)null });

        var (res, doc) = await _alice.GetAsync("/api/sync/cursor");
        Assert.AreEqual(HttpStatusCode.OK, res.StatusCode);
        Assert.AreEqual(2L, doc!.RootElement.GetProperty("highWaterCursor").GetInt64());
    }

    [TestMethod]
    public async Task Pull_PaginatesWithinUntilBoundary()
    {
        for (var i = 0; i < 5; i++)
            await _alice.PostJsonAsync("/api/folders", new { name = $"F{i}", parentFolderId = (Guid?)null });

        var (res, doc) = await _alice.GetAsync("/api/sync/pull?since=0&until=5&limit=2");
        Assert.AreEqual(HttpStatusCode.OK, res.StatusCode);
        var root = doc!.RootElement;
        Assert.AreEqual(2, root.GetProperty("changes").GetArrayLength());
        Assert.AreEqual(2L, root.GetProperty("nextCursor").GetInt64());
        Assert.IsTrue(root.GetProperty("hasMore").GetBoolean());
        Assert.AreEqual(5L, root.GetProperty("highWaterCursor").GetInt64());
        var seqs = root.GetProperty("changes").EnumerateArray().Select(c => c.GetProperty("sequence").GetInt64()).ToList();
        CollectionAssert.AreEqual(new List<long> { 1, 2 }, seqs);
    }

    [TestMethod]
    public async Task Pull_ExcludesChangesBeyondUntil()
    {
        for (var i = 0; i < 3; i++)
            await _alice.PostJsonAsync("/api/folders", new { name = $"G{i}", parentFolderId = (Guid?)null });

        var (res, doc) = await _alice.GetAsync("/api/sync/pull?since=0&until=2&limit=500");
        Assert.AreEqual(HttpStatusCode.OK, res.StatusCode);
        var root = doc!.RootElement;
        Assert.AreEqual(2, root.GetProperty("changes").GetArrayLength());
        Assert.AreEqual(2L, root.GetProperty("nextCursor").GetInt64());
        Assert.IsFalse(root.GetProperty("hasMore").GetBoolean());
    }

    [TestMethod]
    public async Task Pull_EmptyRange_ReturnsSinceAsCursor()
    {
        var (res, doc) = await _alice.GetAsync("/api/sync/pull?since=0&until=0&limit=500");
        Assert.AreEqual(HttpStatusCode.OK, res.StatusCode);
        var root = doc!.RootElement;
        Assert.AreEqual(0, root.GetProperty("changes").GetArrayLength());
        Assert.AreEqual(0L, root.GetProperty("nextCursor").GetInt64());
        Assert.IsFalse(root.GetProperty("hasMore").GetBoolean());
    }

    [TestMethod]
    public async Task Pull_IncludesDeleteTombstoneAfterRowGone()
    {
        var (_, created) = await _alice.PostJsonAsync("/api/folders", new { name = "Doomed", parentFolderId = (Guid?)null });
        var id = created!.RootElement.GetProperty("id").GetGuid();
        var del = await _alice.DeleteAsync($"/api/folders/{id}");
        Assert.AreEqual(HttpStatusCode.NoContent, del.StatusCode);

        var (res, doc) = await _alice.GetAsync("/api/sync/pull?since=0&until=10&limit=500");
        Assert.AreEqual(HttpStatusCode.OK, res.StatusCode);
        var tomb = doc!.RootElement.GetProperty("changes").EnumerateArray()
            .First(c => c.GetProperty("operation").GetString() == "Deleted");
        Assert.AreEqual(id, tomb.GetProperty("entityId").GetGuid());
        Assert.AreEqual("Doomed", tomb.GetProperty("name").GetString());
        Assert.AreEqual("Folder", tomb.GetProperty("entityType").GetString());
    }

    [TestMethod]
    public async Task Pull_IsUserIsolated()
    {
        await _alice.PostJsonAsync("/api/folders", new { name = "AliceOnly", parentFolderId = (Guid?)null });

        var (res, doc) = await _bob.GetAsync("/api/sync/pull?since=0&until=100&limit=500");
        Assert.AreEqual(HttpStatusCode.OK, res.StatusCode);
        Assert.AreEqual(0, doc!.RootElement.GetProperty("changes").GetArrayLength());
    }

    [TestMethod]
    public async Task Pull_UntilBeforeSince_Returns400()
    {
        var (res, _) = await _alice.GetAsync("/api/sync/pull?since=5&until=3&limit=500");
        Assert.AreEqual(HttpStatusCode.BadRequest, res.StatusCode);
    }

    [TestMethod]
    public async Task Pull_MissingUntil_Returns400()
    {
        var (res, _) = await _alice.GetAsync("/api/sync/pull?since=0&limit=500");
        Assert.AreEqual(HttpStatusCode.BadRequest, res.StatusCode);
    }

    [TestMethod]
    public async Task Pull_LimitClampedToMax()
    {
        for (var i = 0; i < 3; i++)
            await _alice.PostJsonAsync("/api/folders", new { name = $"H{i}", parentFolderId = (Guid?)null });

        var (res, doc) = await _alice.GetAsync("/api/sync/pull?since=0&until=100&limit=1000000");
        Assert.AreEqual(HttpStatusCode.OK, res.StatusCode);
        Assert.AreEqual(3, doc!.RootElement.GetProperty("changes").GetArrayLength());
        Assert.IsFalse(doc.RootElement.GetProperty("hasMore").GetBoolean());
    }

    [TestMethod]
    public async Task Pull_RevokedDevice_Returns403()
    {
        var doomed = new ApiClient(_factory.CreateClient());
        await doomed.RegisterAsync("sync-doomed@nexsync.dev", "password123", "Doomed");
        doomed.DeviceId = await RegisterDeviceAsync(doomed, "Doomed PC");
        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<NexSync.Infrastructure.Data.AppDbContext>();
            var dev = await db.Devices.FindAsync(doomed.DeviceId!.Value);
            dev!.RevokedAt = DateTime.UtcNow;
            await db.SaveChangesAsync();
        }
        var (res, _) = await doomed.GetAsync("/api/sync/pull?since=0&until=100&limit=500");
        Assert.AreEqual(HttpStatusCode.Forbidden, res.StatusCode);

        var (cres, _) = await doomed.GetAsync("/api/sync/cursor");
        Assert.AreEqual(HttpStatusCode.Forbidden, cres.StatusCode);
    }

    [TestMethod]
    public async Task Cursor_EmptyHistory_ReturnsZero()
    {
        var (res, doc) = await _alice.GetAsync("/api/sync/cursor");
        Assert.AreEqual(HttpStatusCode.OK, res.StatusCode);
        Assert.AreEqual(0L, doc!.RootElement.GetProperty("highWaterCursor").GetInt64());
    }

    [TestMethod]
    public async Task Pull_SecondPage_ContinuesFromNextCursor()
    {
        for (var i = 0; i < 5; i++)
            await _alice.PostJsonAsync("/api/folders", new { name = $"P{i}", parentFolderId = (Guid?)null });

        var (_, p1) = await _alice.GetAsync("/api/sync/pull?since=0&until=5&limit=2");
        var cursor = p1!.RootElement.GetProperty("nextCursor").GetInt64();
        Assert.AreEqual(2L, cursor);
        Assert.IsTrue(p1.RootElement.GetProperty("hasMore").GetBoolean());

        var (res, p2) = await _alice.GetAsync($"/api/sync/pull?since={cursor}&until=5&limit=2");
        Assert.AreEqual(HttpStatusCode.OK, res.StatusCode);
        var seqs = p2!.RootElement.GetProperty("changes").EnumerateArray().Select(c => c.GetProperty("sequence").GetInt64()).ToList();
        CollectionAssert.AreEqual(new List<long> { 3, 4 }, seqs);
        Assert.AreEqual(4L, p2.RootElement.GetProperty("nextCursor").GetInt64());
        Assert.IsTrue(p2.RootElement.GetProperty("hasMore").GetBoolean());

        var (_, p3) = await _alice.GetAsync("/api/sync/pull?since=4&until=5&limit=2");
        Assert.AreEqual(1, p3!.RootElement.GetProperty("changes").GetArrayLength());
        Assert.AreEqual(5L, p3.RootElement.GetProperty("nextCursor").GetInt64());
        Assert.IsFalse(p3.RootElement.GetProperty("hasMore").GetBoolean());
    }

    [TestMethod]
    public async Task Pull_InvalidDeviceUuid_Returns400()
    {
        var req = new HttpRequestMessage(HttpMethod.Get, "/api/sync/pull?since=0&until=10&limit=10");
        req.Headers.Add("X-Device-Id", "not-a-uuid");
        var res = await _alice.SendRawAsync(req);
        Assert.AreEqual(HttpStatusCode.BadRequest, res.StatusCode);
    }

    [TestMethod]
    public async Task Pull_ForeignDevice_Returns403()
    {
        var other = new ApiClient(_factory.CreateClient());
        await other.RegisterAsync("sync-foreign@nexsync.dev", "password123", "Foreign");
        var foreignId = await RegisterDeviceAsync(other, "Foreign Dev");
        _alice.DeviceId = foreignId;
        var (res, _) = await _alice.GetAsync("/api/sync/pull?since=0&until=10&limit=10");
        Assert.AreEqual(HttpStatusCode.Forbidden, res.StatusCode);
    }

    [TestMethod]
    public async Task Pull_ZeroLimit_Returns400()
    {
        var (res, _) = await _alice.GetAsync("/api/sync/pull?since=0&until=10&limit=0");
        Assert.AreEqual(HttpStatusCode.BadRequest, res.StatusCode);
    }

    [TestMethod]
    public async Task Pull_MissingDevice_Returns400()
    {
        var naked = new ApiClient(_factory.CreateClient());
        await naked.RegisterAsync("sync-naked@nexsync.dev", "password123", "Naked");
        var (res, _) = await naked.GetAsync("/api/sync/pull?since=0&until=100&limit=500");
        Assert.AreEqual(HttpStatusCode.BadRequest, res.StatusCode);
    }

    [TestMethod]
    public async Task Pull_ChangeItemCarriesSnapshotFields()
    {
        await _alice.UploadAsync(System.Text.Encoding.UTF8.GetBytes("hello"), "snap.txt", "text/plain");
        var (res, doc) = await _alice.GetAsync("/api/sync/pull?since=0&until=10&limit=500");
        Assert.AreEqual(HttpStatusCode.OK, res.StatusCode);
        var item = doc!.RootElement.GetProperty("changes").EnumerateArray().First();
        Assert.AreEqual("File", item.GetProperty("entityType").GetString());
        Assert.AreEqual("Created", item.GetProperty("operation").GetString());
        Assert.AreEqual("snap.txt", item.GetProperty("name").GetString());
        Assert.IsFalse(string.IsNullOrEmpty(item.GetProperty("hash").GetString()));
        Assert.AreEqual(5L, item.GetProperty("size").GetInt64());
        Assert.AreEqual("text/plain", item.GetProperty("contentType").GetString());
        Assert.AreEqual(_alice.DeviceId!.Value, item.GetProperty("originDeviceId").GetGuid());
    }
}
