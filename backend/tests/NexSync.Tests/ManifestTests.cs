using System.Net;
using System.Net.Http.Json;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace NexSync.Tests;

[TestClass]
public sealed class ManifestTests
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
        await _alice.RegisterAsync("man-alice@nexsync.dev", "password123", "Alice");
        await _bob.RegisterAsync("man-bob@nexsync.dev", "password123", "Bob");
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
    public async Task Manifest_EmptyWorkspace_ReturnsZeroCursorAndEmptyLists()
    {
        var (res, doc) = await _alice.GetAsync("/api/sync/manifest");
        Assert.AreEqual(HttpStatusCode.OK, res.StatusCode);
        var root = doc!.RootElement;
        Assert.AreEqual(0L, root.GetProperty("snapshotCursor").GetInt64());
        Assert.AreEqual(0, root.GetProperty("folders").GetArrayLength());
        Assert.AreEqual(0, root.GetProperty("files").GetArrayLength());
    }

    [TestMethod]
    public async Task Manifest_ReturnsNestedFoldersFilesAndCursor()
    {
        var (_, p) = await _alice.PostJsonAsync("/api/folders", new { name = "Projects", parentFolderId = (Guid?)null });
        var pId = p!.RootElement.GetProperty("id").GetGuid();
        var (_, c) = await _alice.PostJsonAsync("/api/folders", new { name = "Form4x", parentFolderId = pId });
        var cId = c!.RootElement.GetProperty("id").GetGuid();
        var (_, f) = await _alice.UploadAsync(Encoding.UTF8.GetBytes("home"), "home.dart", "text/plain", cId);
        var fId = f!.RootElement.GetProperty("id").GetGuid();

        var (res, doc) = await _alice.GetAsync("/api/sync/manifest");
        Assert.AreEqual(HttpStatusCode.OK, res.StatusCode);
        var root = doc!.RootElement;
        Assert.AreEqual(3L, root.GetProperty("snapshotCursor").GetInt64());

        var folders = root.GetProperty("folders").EnumerateArray().ToList();
        Assert.AreEqual(2, folders.Count);
        var byName = folders.ToDictionary(x => x.GetProperty("name").GetString()!);
        Assert.AreEqual(pId, byName["Projects"].GetProperty("id").GetGuid());
        Assert.IsTrue(string.IsNullOrEmpty(byName["Projects"].GetProperty("parentFolderId").GetString()));
        Assert.AreEqual(pId, byName["Form4x"].GetProperty("parentFolderId").GetGuid());

        var files = root.GetProperty("files").EnumerateArray().ToList();
        Assert.AreEqual(1, files.Count);
        Assert.AreEqual(fId, files[0].GetProperty("id").GetGuid());
        Assert.AreEqual("home.dart", files[0].GetProperty("name").GetString());
        Assert.AreEqual(cId, files[0].GetProperty("folderId").GetGuid());
        Assert.IsFalse(string.IsNullOrEmpty(files[0].GetProperty("hash").GetString()));
        Assert.AreEqual(4L, files[0].GetProperty("size").GetInt64());
        Assert.AreEqual("text/plain", files[0].GetProperty("contentType").GetString());
    }

    [TestMethod]
    public async Task Manifest_IsUserIsolated()
    {
        await _alice.PostJsonAsync("/api/folders", new { name = "AliceOnly", parentFolderId = (Guid?)null });
        var (res, doc) = await _bob.GetAsync("/api/sync/manifest");
        Assert.AreEqual(HttpStatusCode.OK, res.StatusCode);
        var root = doc!.RootElement;
        Assert.AreEqual(0, root.GetProperty("folders").GetArrayLength());
        Assert.AreEqual(0, root.GetProperty("files").GetArrayLength());
        Assert.AreEqual(0L, root.GetProperty("snapshotCursor").GetInt64());
    }

    [TestMethod]
    public async Task Manifest_ThenPullSinceCursor_YieldsConcurrentChangeExactlyOnce()
    {
        await _alice.PostJsonAsync("/api/folders", new { name = "Base", parentFolderId = (Guid?)null });

        var (mres, mdoc) = await _alice.GetAsync("/api/sync/manifest");
        Assert.AreEqual(HttpStatusCode.OK, mres.StatusCode);
        var cursor = mdoc!.RootElement.GetProperty("snapshotCursor").GetInt64();
        Assert.AreEqual(1L, cursor);

        await _alice.PostJsonAsync("/api/folders", new { name = "After", parentFolderId = (Guid?)null });

        var (pres, pdoc) = await _alice.GetAsync($"/api/sync/pull?since={cursor}&until={cursor + 10}&limit=500");
        Assert.AreEqual(HttpStatusCode.OK, pres.StatusCode);
        var changes = pdoc!.RootElement.GetProperty("changes").EnumerateArray().ToList();
        Assert.AreEqual(1, changes.Count);
        Assert.AreEqual("After", changes[0].GetProperty("name").GetString());
        Assert.AreEqual(cursor + 1, changes[0].GetProperty("sequence").GetInt64());
        Assert.AreEqual(cursor + 1, pdoc.RootElement.GetProperty("nextCursor").GetInt64());
    }

    [TestMethod]
    public async Task Manifest_RequiresValidDevice()
    {
        var naked = new ApiClient(_factory.CreateClient());
        await naked.RegisterAsync("man-naked@nexsync.dev", "password123", "Naked");
        var (res, _) = await naked.GetAsync("/api/sync/manifest");
        Assert.AreEqual(HttpStatusCode.BadRequest, res.StatusCode);

        var before = _alice.DeviceId;
        _alice.DeviceId = Guid.NewGuid();
        var (res2, _) = await _alice.GetAsync("/api/sync/manifest");
        Assert.AreEqual(HttpStatusCode.Forbidden, res2.StatusCode);
        _alice.DeviceId = before;
    }

    [TestMethod]
    public async Task Manifest_RevokedDevice_Returns403()
    {
        var doomed = new ApiClient(_factory.CreateClient());
        await doomed.RegisterAsync("man-doomed@nexsync.dev", "password123", "Doomed");
        doomed.DeviceId = await RegisterDeviceAsync(doomed, "Doomed PC");
        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<NexSync.Infrastructure.Data.AppDbContext>();
            var dev = await db.Devices.FindAsync(doomed.DeviceId!.Value);
            dev!.RevokedAt = DateTime.UtcNow;
            await db.SaveChangesAsync();
        }
        var (res, _) = await doomed.GetAsync("/api/sync/manifest");
        Assert.AreEqual(HttpStatusCode.Forbidden, res.StatusCode);
    }

    [TestMethod]
    public async Task Manifest_IncludesLegacyDataWithoutChangeLog()
    {
        var aliceId = await UserIdAsync("man-alice@nexsync.dev");
        var legacyFolderId = Guid.NewGuid();
        var legacyFileId = Guid.NewGuid();
        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<NexSync.Infrastructure.Data.AppDbContext>();
            db.Folders.Add(new NexSync.Domain.Entities.Folder
            {
                Id = legacyFolderId, Name = "Legacy", ParentFolderId = null, OwnerId = aliceId,
                CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow
            });
            db.Files.Add(new NexSync.Domain.Entities.FileEntry
            {
                Id = legacyFileId, Name = "old.bin", FolderId = legacyFolderId, OwnerId = aliceId,
                Hash = new string('a', 64), Size = 10, ContentType = "application/octet-stream",
                StoragePath = "/nonexistent", CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow
            });
            await db.SaveChangesAsync();
        }

        var (res, doc) = await _alice.GetAsync("/api/sync/manifest");
        Assert.AreEqual(HttpStatusCode.OK, res.StatusCode);
        var root = doc!.RootElement;
        Assert.AreEqual(0L, root.GetProperty("snapshotCursor").GetInt64());
        var folderIds = root.GetProperty("folders").EnumerateArray().Select(f => f.GetProperty("id").GetGuid()).ToList();
        Assert.IsTrue(folderIds.Contains(legacyFolderId));
        var file = root.GetProperty("files").EnumerateArray().First(f => f.GetProperty("id").GetGuid() == legacyFileId);
        Assert.AreEqual("old.bin", file.GetProperty("name").GetString());

        var (pres, pdoc) = await _alice.GetAsync("/api/sync/pull?since=0&until=10&limit=500");
        Assert.AreEqual(HttpStatusCode.OK, pres.StatusCode);
        Assert.AreEqual(0, pdoc!.RootElement.GetProperty("changes").GetArrayLength());
    }

    [TestMethod]
    public async Task Manifest_ConcurrentWrite_DoesNotLeakIntoSnapshot()
    {
        await _alice.PostJsonAsync("/api/folders", new { name = "Settled", parentFolderId = (Guid?)null });

        var manifestTask = _alice.GetAsync("/api/sync/manifest");
        await Task.Delay(50);
        var (_, createdFolder) = await _alice.PostJsonAsync("/api/folders", new { name = "Racer", parentFolderId = (Guid?)null });
        var racerFolderId = createdFolder!.RootElement.GetProperty("id").GetGuid();
        var (_, createdFile) = await _alice.UploadAsync(Encoding.UTF8.GetBytes("race"), "race.txt", "text/plain", racerFolderId);
        var racerFileId = createdFile!.RootElement.GetProperty("id").GetGuid();
        var (mres, mdoc) = await manifestTask;
        Assert.AreEqual(HttpStatusCode.OK, mres.StatusCode);
        var cursor = mdoc!.RootElement.GetProperty("snapshotCursor").GetInt64();
        var manifestFolderIds = mdoc.RootElement.GetProperty("folders").EnumerateArray()
            .Select(f => f.GetProperty("id").GetGuid()).ToHashSet();
        var manifestFileIds = mdoc.RootElement.GetProperty("files").EnumerateArray()
            .Select(f => f.GetProperty("id").GetGuid()).ToHashSet();

        var (mres2, mdoc2) = await _alice.GetAsync("/api/sync/manifest");
        Assert.AreEqual(HttpStatusCode.OK, mres2.StatusCode);
        Assert.AreEqual(mdoc2!.RootElement.GetProperty("snapshotCursor").GetInt64(),
            (await _alice.GetAsync("/api/sync/cursor")).Item2!.RootElement.GetProperty("highWaterCursor").GetInt64());

        var (pres, pdoc) = await _alice.GetAsync($"/api/sync/pull?since={cursor}&until={cursor + 10}&limit=500");
        Assert.AreEqual(HttpStatusCode.OK, pres.StatusCode);
        var pulledIds = pdoc!.RootElement.GetProperty("changes").EnumerateArray()
            .Select(c => c.GetProperty("entityId").GetGuid()).ToHashSet();

        var folderInManifest = manifestFolderIds.Contains(racerFolderId);
        var fileInManifest = manifestFileIds.Contains(racerFileId);
        Assert.AreEqual(folderInManifest, fileInManifest,
            "Snapshot must be atomic: racer folder and its file appear together or not at all.");
        Assert.IsTrue(manifestFolderIds.Contains(racerFolderId) != pulledIds.Contains(racerFolderId),
            "Racer folder must appear in exactly one of manifest or follow-up pull.");
        Assert.IsTrue(manifestFileIds.Contains(racerFileId) != pulledIds.Contains(racerFileId),
            "Racer file must appear in exactly one of manifest or follow-up pull.");
        foreach (var id in pulledIds.Where(id => id == racerFolderId || id == racerFileId))
        {
            var seq = pdoc.RootElement.GetProperty("changes").EnumerateArray()
                .First(c => c.GetProperty("entityId").GetGuid() == id).GetProperty("sequence").GetInt64();
            Assert.IsTrue(seq > cursor, "Pulled racer change must be newer than the snapshot cursor.");
        }
    }

    private async Task<Guid> UserIdAsync(string email)
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<NexSync.Infrastructure.Data.AppDbContext>();
        return (await db.Users.SingleAsync(u => u.Email == email)).Id;
    }

    [TestMethod]
    public async Task Manifest_DoesNotConsumeChangeLog()
    {
        await _alice.PostJsonAsync("/api/folders", new { name = "Keep", parentFolderId = (Guid?)null });
        await _alice.GetAsync("/api/sync/manifest");
        await _alice.GetAsync("/api/sync/manifest");
        var (res, doc) = await _alice.GetAsync("/api/sync/pull?since=0&until=10&limit=500");
        Assert.AreEqual(HttpStatusCode.OK, res.StatusCode);
        Assert.AreEqual(1, doc!.RootElement.GetProperty("changes").GetArrayLength());
    }
}
