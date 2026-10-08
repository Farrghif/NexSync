using System.Net;
using System.Net.Http.Json;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using NexSync.Domain.Entities;
using NexSync.Infrastructure.Data;

namespace NexSync.Tests;

[TestClass]
public sealed class MutationChangeLogTests
{
    private readonly NexSyncApiFactory _factory = new();
    private ApiClient _client = null!;

    [TestInitialize]
    public async Task InitializeAsync()
    {
        await _factory.InitializeAsync();
        _client = new ApiClient(_factory.CreateClient());
        await _client.RegisterAsync("changelog@nexsync.dev", "password123", "Changelog User");
    }

    [TestCleanup]
    public async Task DisposeAsync() => await _factory.DisposeAsync();

    private async Task<List<ChangeLog>> ChangeLogsAsync(Guid userId)
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return await db.ChangeLogs.Where(c => c.UserId == userId).OrderBy(c => c.Sequence).ToListAsync();
    }

    private static Guid UserIdFromToken(ApiClient client)
    {
        var parts = client.AccessToken!.Split('.');
        var payload = parts[1].Replace('-', '+').Replace('_', '/');
        payload += new string('=', (4 - payload.Length % 4) % 4);
        using var doc = System.Text.Json.JsonDocument.Parse(Convert.FromBase64String(payload));
        return Guid.Parse(doc.RootElement.GetProperty("http://schemas.xmlsoap.org/ws/2005/05/identity/claims/nameidentifier").GetString()!);
    }

    private async Task<Guid> RegisterDeviceAsync(string name)
    {
        var req = new HttpRequestMessage(HttpMethod.Post, "/api/devices")
        {
            Content = JsonContent.Create(new { name, platform = "Windows" })
        };
        req.Headers.Add("X-Operation-Id", Guid.NewGuid().ToString());
        var res = await _client.SendRawAsync(req);
        Assert.AreEqual(HttpStatusCode.Created, res.StatusCode);
        var doc = await ApiClient.ParseAsync(res);
        return doc!.RootElement.GetProperty("deviceId").GetGuid();
    }

    private async Task<(HttpResponseMessage, System.Text.Json.JsonDocument?)> MutateWithDeviceAsync(HttpRequestMessage req, Guid deviceId)
    {
        req.Headers.Add("X-Device-Id", deviceId.ToString());
        var res = await _client.SendRawAsync(req);
        return (res, await ApiClient.ParseAsync(res));
    }

    [TestMethod]
    public async Task CreateFolder_WritesCreatedChangeWithSnapshot()
    {
        var userId = UserIdFromToken(_client);
        var (res, doc) = await _client.PostJsonAsync("/api/folders", new { name = "Projects", parentFolderId = (Guid?)null });
        Assert.AreEqual(HttpStatusCode.Created, res.StatusCode);
        var changes = await ChangeLogsAsync(userId);
        Assert.AreEqual(1, changes.Count);
        var c = changes[0];
        Assert.AreEqual(SyncEntityType.Folder, c.EntityType);
        Assert.AreEqual(SyncOperation.Created, c.Operation);
        Assert.AreEqual(1L, c.Sequence);
        Assert.AreEqual(doc!.RootElement.GetProperty("id").GetGuid(), c.EntityId);
        Assert.AreEqual("Projects", c.Name);
        Assert.IsNull(c.ParentFolderId);
        Assert.IsNull(c.OriginDeviceId);
    }

    [TestMethod]
    public async Task RenameFolder_WritesRenamedChange()
    {
        var userId = UserIdFromToken(_client);
        var (_, created) = await _client.PostJsonAsync("/api/folders", new { name = "Old", parentFolderId = (Guid?)null });
        var id = created!.RootElement.GetProperty("id").GetGuid();
        var (res, _) = await _client.PutJsonAsync($"/api/folders/{id}", new { name = "New", parentFolderId = (Guid?)null });
        Assert.AreEqual(HttpStatusCode.OK, res.StatusCode);
        var changes = await ChangeLogsAsync(userId);
        Assert.AreEqual(2, changes.Count);
        Assert.AreEqual(SyncOperation.Renamed, changes[1].Operation);
        Assert.AreEqual("New", changes[1].Name);
        Assert.AreEqual(2L, changes[1].Sequence);
    }

    [TestMethod]
    public async Task MoveFolder_WritesMovedChangeWithNewParent()
    {
        var userId = UserIdFromToken(_client);
        var (_, a) = await _client.PostJsonAsync("/api/folders", new { name = "A", parentFolderId = (Guid?)null });
        var (_, b) = await _client.PostJsonAsync("/api/folders", new { name = "B", parentFolderId = (Guid?)null });
        var aId = a!.RootElement.GetProperty("id").GetGuid();
        var bId = b!.RootElement.GetProperty("id").GetGuid();
        var (res, _) = await _client.PutJsonAsync($"/api/folders/{aId}", new { name = "A", parentFolderId = bId });
        Assert.AreEqual(HttpStatusCode.OK, res.StatusCode);
        var changes = await ChangeLogsAsync(userId);
        var moved = changes[^1];
        Assert.AreEqual(SyncOperation.Moved, moved.Operation);
        Assert.AreEqual(bId, moved.ParentFolderId);
    }

    [TestMethod]
    public async Task DeleteFile_LeavesTombstoneAfterRowGone()
    {
        var userId = UserIdFromToken(_client);
        var (_, doc) = await _client.UploadAsync(Encoding.UTF8.GetBytes("data"), "note.txt", "text/plain");
        var id = doc!.RootElement.GetProperty("id").GetGuid();
        var del = await _client.DeleteAsync($"/api/files/{id}");
        Assert.AreEqual(HttpStatusCode.NoContent, del.StatusCode);
        var changes = await ChangeLogsAsync(userId);
        Assert.AreEqual(2, changes.Count);
        var tomb = changes[1];
        Assert.AreEqual(SyncOperation.Deleted, tomb.Operation);
        Assert.AreEqual(SyncEntityType.File, tomb.EntityType);
        Assert.AreEqual("note.txt", tomb.Name);
        Assert.IsNull(tomb.ParentFolderId);
        Assert.IsNull(tomb.Hash);
        Assert.IsNull(tomb.Size);
        Assert.IsNull(tomb.ContentType);
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.IsNull(await db.Files.FindAsync(id));
    }

    [TestMethod]
    public async Task RecursiveFolderDelete_EmitsOneTombstonePerEntity()
    {
        var userId = UserIdFromToken(_client);
        var (_, p) = await _client.PostJsonAsync("/api/folders", new { name = "P", parentFolderId = (Guid?)null });
        var pId = p!.RootElement.GetProperty("id").GetGuid();
        var (_, c) = await _client.PostJsonAsync("/api/folders", new { name = "C", parentFolderId = pId });
        var cId = c!.RootElement.GetProperty("id").GetGuid();
        await _client.UploadAsync([1, 2], "f1.bin", "application/octet-stream", pId);
        await _client.UploadAsync([3, 4], "f2.bin", "application/octet-stream", cId);
        var before = (await ChangeLogsAsync(userId)).Count;
        var del = await _client.DeleteAsync($"/api/folders/{pId}");
        Assert.AreEqual(HttpStatusCode.NoContent, del.StatusCode);
        var changes = await ChangeLogsAsync(userId);
        var tombstones = changes.Skip(before).ToList();
        Assert.AreEqual(4, tombstones.Count);
        Assert.IsTrue(tombstones.All(t => t.Operation == SyncOperation.Deleted));
        var folderTombs = tombstones.Where(t => t.EntityType == SyncEntityType.Folder).Select(t => t.EntityId).ToHashSet();
        Assert.IsTrue(folderTombs.SetEquals([pId, cId]));
        foreach (var t in tombstones)
        {
            Assert.IsNotNull(t.Name);
            Assert.IsNull(t.Hash);
            Assert.IsNull(t.Size);
            Assert.IsNull(t.ContentType);
        }
        var byEntity = tombstones.ToDictionary(t => t.EntityId);
        Assert.IsNull(byEntity[pId].ParentFolderId);
        Assert.AreEqual(pId, byEntity[cId].ParentFolderId);
        var fileTombs = tombstones.Where(t => t.EntityType == SyncEntityType.File).ToList();
        Assert.AreEqual(2, fileTombs.Count);
        Assert.IsTrue(fileTombs.Any(t => t.Name == "f1.bin" && t.ParentFolderId == pId));
        Assert.IsTrue(fileTombs.Any(t => t.Name == "f2.bin" && t.ParentFolderId == cId));
        var folderSeqs = tombstones.Where(t => t.EntityType == SyncEntityType.Folder).Select(t => t.Sequence).ToList();
        var fileSeqs = tombstones.Where(t => t.EntityType == SyncEntityType.File).Select(t => t.Sequence).ToList();
        Assert.IsTrue(fileSeqs.Max() < folderSeqs.Min());
    }

    [TestMethod]
    public async Task FailedMutation_WritesNoEntityAndNoChange()
    {
        var userId = UserIdFromToken(_client);
        var (res, _) = await _client.PostJsonAsync("/api/folders", new { name = "CON", parentFolderId = (Guid?)null });
        Assert.AreEqual(HttpStatusCode.BadRequest, res.StatusCode);
        var changes = await ChangeLogsAsync(userId);
        Assert.AreEqual(0, changes.Count);
    }

    private sealed class ThrowingAllocator : NexSync.Application.Interfaces.ISyncSequenceAllocator
    {
        public Task<long> AllocateAsync(Guid userId, CancellationToken cancellationToken = default)
            => throw new InvalidOperationException("Simulated allocator failure mid-transaction.");
    }

    [TestMethod]
    public async Task MidTransactionFailure_WritesNeitherEntityNorChange()
    {
        var userId = UserIdFromToken(_client);
        await using var scope = _factory.Services.CreateAsyncScope();
        var sp = scope.ServiceProvider;
        var folders = new NexSync.Application.Services.FolderService(
            sp.GetRequiredService<NexSync.Application.Interfaces.IFolderRepository>(),
            sp.GetRequiredService<NexSync.Application.Interfaces.IFileRepository>(),
            sp.GetRequiredService<NexSync.Application.Interfaces.IStorageService>(),
            sp.GetRequiredService<NexSync.Application.Interfaces.ITransactionProvider>(),
            new NexSync.Infrastructure.Data.SyncChangeWriter(
                sp.GetRequiredService<NexSync.Infrastructure.Data.AppDbContext>(),
                new ThrowingAllocator()),
            sp.GetRequiredService<NexSync.Application.Interfaces.IIdempotencyStore>());
        await Assert.ThrowsExactlyAsync<InvalidOperationException>(
            async () => await folders.CreateAsync(userId, "Doomed", null));
        await using var verify = _factory.Services.CreateAsyncScope();
        var db = verify.ServiceProvider.GetRequiredService<NexSync.Infrastructure.Data.AppDbContext>();
        Assert.AreEqual(0, await db.Folders.CountAsync(f => f.OwnerId == userId));
        Assert.AreEqual(0, (await ChangeLogsAsync(userId)).Count);
    }

    [TestMethod]
    public async Task DeviceId_PropagatesToOriginDeviceId()
    {
        var userId = UserIdFromToken(_client);
        var deviceId = await RegisterDeviceAsync("Laptop");
        var req = new HttpRequestMessage(HttpMethod.Post, "/api/folders")
        {
            Content = JsonContent.Create(new { name = "FromDevice", parentFolderId = (Guid?)null })
        };
        var (res, _) = await MutateWithDeviceAsync(req, deviceId);
        Assert.AreEqual(HttpStatusCode.Created, res.StatusCode);
        var changes = await ChangeLogsAsync(userId);
        Assert.AreEqual(1, changes.Count);
        Assert.AreEqual(deviceId, changes[0].OriginDeviceId);
    }

    [TestMethod]
    public async Task RevokedDevice_Returns403()
    {
        var deviceId = await RegisterDeviceAsync("Doomed");
        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var dev = await db.Devices.FindAsync(deviceId);
            dev!.RevokedAt = DateTime.UtcNow;
            await db.SaveChangesAsync();
        }
        var req = new HttpRequestMessage(HttpMethod.Post, "/api/folders")
        {
            Content = JsonContent.Create(new { name = "Nope", parentFolderId = (Guid?)null })
        };
        var (res, _) = await MutateWithDeviceAsync(req, deviceId);
        Assert.AreEqual(HttpStatusCode.Forbidden, res.StatusCode);
    }

    [TestMethod]
    public async Task ForeignDevice_Returns403()
    {
        var other = new ApiClient(_factory.CreateClient());
        await other.RegisterAsync("other@nexsync.dev", "password123", "Other");
        var req = new HttpRequestMessage(HttpMethod.Post, "/api/devices")
        {
            Content = JsonContent.Create(new { name = "OtherDev", platform = "Web" })
        };
        req.Headers.Add("X-Operation-Id", Guid.NewGuid().ToString());
        var res = await other.SendRawAsync(req);
        var doc = await ApiClient.ParseAsync(res);
        var foreignId = doc!.RootElement.GetProperty("deviceId").GetGuid();
        var mut = new HttpRequestMessage(HttpMethod.Post, "/api/folders")
        {
            Content = JsonContent.Create(new { name = "Nope2", parentFolderId = (Guid?)null })
        };
        var (res2, _) = await MutateWithDeviceAsync(mut, foreignId);
        Assert.AreEqual(HttpStatusCode.Forbidden, res2.StatusCode);
    }

    [TestMethod]
    public async Task Upload_WritesCreatedChangeWithHashSnapshot()
    {
        var userId = UserIdFromToken(_client);
        var payload = Encoding.UTF8.GetBytes("sync me");
        var (res, _) = await _client.UploadAsync(payload, "sync.txt", "text/plain");
        Assert.AreEqual(HttpStatusCode.Created, res.StatusCode);
        var changes = await ChangeLogsAsync(userId);
        Assert.AreEqual(1, changes.Count);
        var c = changes[0];
        Assert.AreEqual(SyncOperation.Created, c.Operation);
        Assert.AreEqual(SyncEntityType.File, c.EntityType);
        Assert.IsNotNull(c.Hash);
        Assert.AreEqual(payload.Length, c.Size);
        Assert.AreEqual("text/plain", c.ContentType);
    }
}
