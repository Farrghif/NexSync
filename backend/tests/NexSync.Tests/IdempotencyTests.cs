using System.Net;
using System.Net.Http.Json;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using NexSync.Domain.Entities;
using NexSync.Infrastructure.Data;

namespace NexSync.Tests;

[TestClass]
public sealed class IdempotencyTests
{
    private readonly NexSyncApiFactory _factory = new();
    private ApiClient _client = null!;

    [TestInitialize]
    public async Task InitializeAsync()
    {
        await _factory.InitializeAsync();
        _client = new ApiClient(_factory.CreateClient());
        await _client.RegisterAsync("idem@nexsync.dev", "password123", "Idem User");
    }

    [TestCleanup]
    public async Task DisposeAsync() => await _factory.DisposeAsync();

    private static Guid UserIdFromToken(ApiClient client)
    {
        var parts = client.AccessToken!.Split('.');
        var payload = parts[1].Replace('-', '+').Replace('_', '/');
        payload += new string('=', (4 - payload.Length % 4) % 4);
        using var doc = System.Text.Json.JsonDocument.Parse(Convert.FromBase64String(payload));
        return Guid.Parse(doc.RootElement.GetProperty("http://schemas.xmlsoap.org/ws/2005/05/identity/claims/nameidentifier").GetString()!);
    }

    private async Task<List<ChangeLog>> ChangeLogsAsync(Guid userId)
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return await db.ChangeLogs.Where(c => c.UserId == userId).OrderBy(c => c.Sequence).ToListAsync();
    }

    private async Task<int> ProcessedCountAsync()
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return await db.ProcessedOperations.CountAsync();
    }

    private async Task<int> FoldersNamedAsync(Guid userId, string name)
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return await db.Folders.CountAsync(f => f.OwnerId == userId && f.Name == name);
    }

    [TestMethod]
    public async Task CreateFolder_WithOperationId_ThenIdenticalRetry_ReplaysWithoutSecondMutation()
    {
        var userId = UserIdFromToken(_client);
        var opId = Guid.NewGuid();
        var body = new { name = "IdemFolder", parentFolderId = (Guid?)null };

        var req1 = new HttpRequestMessage(HttpMethod.Post, "/api/folders") { Content = JsonContent.Create(body) };
        req1.Headers.Add("X-Operation-Id", opId.ToString());
        var res1 = await _client.SendRawAsync(req1);
        var doc1 = await ApiClient.ParseAsync(res1);
        Assert.AreEqual(HttpStatusCode.Created, res1.StatusCode);

        var req2 = new HttpRequestMessage(HttpMethod.Post, "/api/folders") { Content = JsonContent.Create(body) };
        req2.Headers.Add("X-Operation-Id", opId.ToString());
        var res2 = await _client.SendRawAsync(req2);
        var doc2 = await ApiClient.ParseAsync(res2);
        Assert.AreEqual(HttpStatusCode.Created, res2.StatusCode);

        Assert.AreEqual(doc1!.RootElement.GetProperty("id").GetGuid(), doc2!.RootElement.GetProperty("id").GetGuid());
        Assert.AreEqual(1, await FoldersNamedAsync(userId, "IdemFolder"));
        var changes = await ChangeLogsAsync(userId);
        Assert.AreEqual(1, changes.Count);
        Assert.AreEqual(1L, changes[0].Sequence);
        Assert.AreEqual(1, await ProcessedCountAsync());
    }

    [TestMethod]
    public async Task RetryAfterSimulatedTimeout_ReturnsByteIdenticalPayload_NoSecondChange()
    {
        var userId = UserIdFromToken(_client);
        var opId = Guid.NewGuid();
        var body = new { name = "TimeoutFolder", parentFolderId = (Guid?)null };

        var req1 = new HttpRequestMessage(HttpMethod.Post, "/api/folders") { Content = JsonContent.Create(body) };
        req1.Headers.Add("X-Operation-Id", opId.ToString());
        var res1 = await _client.SendRawAsync(req1);
        var payload1 = await res1.Content.ReadAsStringAsync();
        await System.IO.File.WriteAllTextAsync("C:\\Temp\\status1.txt", res1.StatusCode.ToString());

        var req2 = new HttpRequestMessage(HttpMethod.Post, "/api/folders") { Content = JsonContent.Create(body) };
        req2.Headers.Add("X-Operation-Id", opId.ToString());
        var res2 = await _client.SendRawAsync(req2);
        var payload2 = await res2.Content.ReadAsStringAsync();

        Assert.AreEqual(HttpStatusCode.Created, res2.StatusCode);
        var jd1 = await ApiClient.ParseAsync(res1);
        var jd2 = await ApiClient.ParseAsync(res2);
        Assert.AreEqual(
            jd1!.RootElement.GetProperty("id").GetGuid(),
            jd2!.RootElement.GetProperty("id").GetGuid());
        Assert.AreEqual(
            jd1.RootElement.GetProperty("name").GetString(),
            jd2.RootElement.GetProperty("name").GetString());
        Assert.AreEqual(1, (await ChangeLogsAsync(userId)).Count);
    }

    [TestMethod]
    public async Task SameOperationId_DifferentBody_Returns400_OperationIdReuse()
    {
        var opId = Guid.NewGuid();
        var req1 = new HttpRequestMessage(HttpMethod.Post, "/api/folders")
        {
            Content = JsonContent.Create(new { name = "First", parentFolderId = (Guid?)null })
        };
        req1.Headers.Add("X-Operation-Id", opId.ToString());
        var res1 = await _client.SendRawAsync(req1);
        Assert.AreEqual(HttpStatusCode.Created, res1.StatusCode);

        var req2 = new HttpRequestMessage(HttpMethod.Post, "/api/folders")
        {
            Content = JsonContent.Create(new { name = "Second", parentFolderId = (Guid?)null })
        };
        req2.Headers.Add("X-Operation-Id", opId.ToString());
        var res2 = await _client.SendRawAsync(req2);
        Assert.AreEqual(HttpStatusCode.BadRequest, res2.StatusCode);
        var doc = await ApiClient.ParseAsync(res2);
        Assert.AreEqual("OPERATION_ID_REUSE", doc!.RootElement.GetProperty("code").GetString());
    }

    [TestMethod]
    public async Task ConcurrentDuplicateOperationId_ExecutesMutationExactlyOnce()
    {
        var userId = UserIdFromToken(_client);
        var opId = Guid.NewGuid();
        var body = new { name = "Concurrent", parentFolderId = (Guid?)null };

        var tasks = Enumerable.Range(0, 4).Select(async _ =>
        {
            var req = new HttpRequestMessage(HttpMethod.Post, "/api/folders") { Content = JsonContent.Create(body) };
            req.Headers.Add("X-Operation-Id", opId.ToString());
            return await _client.SendRawAsync(req);
        });
        var results = await Task.WhenAll(tasks);

        Assert.IsTrue(results.Count(r => r.StatusCode == HttpStatusCode.Created) >= 1);
        Assert.IsTrue(results.All(r => r.StatusCode is HttpStatusCode.Created or HttpStatusCode.BadRequest));
        Assert.AreEqual(1, await FoldersNamedAsync(userId, "Concurrent"));
        Assert.AreEqual(1, (await ChangeLogsAsync(userId)).Count);
        Assert.AreEqual(1, await ProcessedCountAsync());
    }

    [TestMethod]
    public async Task FailedTransaction_LeavesNoProcessedOperationNoChangeLogNoFolder()
    {
        var userId = UserIdFromToken(_client);
        var opId = Guid.NewGuid();
        var mutation = new NexSync.Application.Interfaces.MutationContext(
            opId, NexSync.Domain.Sync.RequestFingerprint.ForMutation("POST", "/api/folders",
                ("name", "Ghost"), ("parentFolderId", "")), 201);
        await using var scope = _factory.Services.CreateAsyncScope();
        var sp = scope.ServiceProvider;
        var folders = new NexSync.Application.Services.FolderService(
            sp.GetRequiredService<NexSync.Application.Interfaces.IFolderRepository>(),
            sp.GetRequiredService<NexSync.Application.Interfaces.IFileRepository>(),
            sp.GetRequiredService<NexSync.Application.Interfaces.IStorageService>(),
            sp.GetRequiredService<NexSync.Application.Interfaces.ITransactionProvider>(),
            new NexSync.Infrastructure.Data.SyncChangeWriter(
                sp.GetRequiredService<AppDbContext>(),
                new ThrowingAllocator()),
            sp.GetRequiredService<NexSync.Application.Interfaces.IIdempotencyStore>());

        await Assert.ThrowsExactlyAsync<InvalidOperationException>(
            async () => await folders.CreateAsync(userId, "Ghost", null, null, mutation));

        await using var verify = _factory.Services.CreateAsyncScope();
        var db = verify.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.AreEqual(0, await db.Folders.CountAsync(f => f.OwnerId == userId && f.Name == "Ghost"));
        Assert.AreEqual(0, await db.ChangeLogs.CountAsync(c => c.UserId == userId));
        Assert.IsFalse(await db.ProcessedOperations.AnyAsync(p => p.OperationId == opId));
    }

    [TestMethod]
    public async Task SuccessfulMutation_HasExactlyOneChangeLogAndOneProcessedRecord()
    {
        var userId = UserIdFromToken(_client);
        var opId = Guid.NewGuid();
        var req = new HttpRequestMessage(HttpMethod.Post, "/api/folders")
        {
            Content = JsonContent.Create(new { name = "Once", parentFolderId = (Guid?)null })
        };
        req.Headers.Add("X-Operation-Id", opId.ToString());
        var res = await _client.SendRawAsync(req);
        Assert.AreEqual(HttpStatusCode.Created, res.StatusCode);

        var changes = await ChangeLogsAsync(userId);
        Assert.AreEqual(1, changes.Count);
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var record = await db.ProcessedOperations.SingleAsync(p => p.OperationId == opId);
        Assert.AreEqual<long?>(changes[0].Sequence, record.Sequence);
        Assert.AreEqual(changes[0].EntityId, record.EntityId);
        Assert.AreEqual(201, record.ResultStatus);
        Assert.IsNotNull(record.ResultPayload);
    }

    [TestMethod]
    public async Task Upload_WithOperationId_ByteIdenticalRetry_ReplaysSingleFile()
    {
        var userId = UserIdFromToken(_client);
        var opId = Guid.NewGuid();
        var payload = Encoding.UTF8.GetBytes("idempotent upload content");

        async Task<HttpResponseMessage> SendAsync()
        {
            using var form = new MultipartFormDataContent();
            var byteContent = new ByteArrayContent(payload);
            byteContent.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("text/plain");
            form.Add(byteContent, "file", "same.txt");
            var req = new HttpRequestMessage(HttpMethod.Post, "/api/files/upload") { Content = form };
            req.Headers.Add("X-Operation-Id", opId.ToString());
            return await _client.SendRawAsync(req);
        }

        var res1 = await SendAsync();
        var res2 = await SendAsync();
        Assert.AreEqual(HttpStatusCode.Created, res1.StatusCode);
        Assert.AreEqual(HttpStatusCode.Created, res2.StatusCode);
        var d1 = await ApiClient.ParseAsync(res1);
        var d2 = await ApiClient.ParseAsync(res2);
        Assert.AreEqual(d1!.RootElement.GetProperty("id").GetGuid(), d2!.RootElement.GetProperty("id").GetGuid());
        Assert.AreEqual(1, await FileCountAsync(userId, "same.txt"));
        var changes = await ChangeLogsAsync(userId);
        Assert.AreEqual(1, changes.Count);
        Assert.AreEqual(1L, changes[0].Sequence);
    }

    private async Task<int> FileCountAsync(Guid userId, string name)
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return await db.Files.CountAsync(f => f.OwnerId == userId && f.Name == name);
    }

    private sealed class ThrowingAllocator : NexSync.Application.Interfaces.ISyncSequenceAllocator
    {
        public Task<long> AllocateAsync(Guid userId, CancellationToken cancellationToken = default)
            => throw new InvalidOperationException("Simulated allocator failure mid-transaction.");
    }
}
