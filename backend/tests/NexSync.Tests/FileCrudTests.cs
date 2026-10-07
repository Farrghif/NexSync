using System.Net;
using System.Text;

namespace NexSync.Tests;

[TestClass]
public sealed class FileCrudTests 
{
    private readonly NexSyncApiFactory _factory = new();
    private ApiClient _client = null!;

    [TestInitialize]
    public async Task InitializeAsync()
    {
        await _factory.InitializeAsync();
        _client = new ApiClient(_factory.CreateClient());
        await _client.RegisterAsync("file@nexsync.dev", "password123", "File User");
    }

    [TestCleanup]
    public async Task DisposeAsync() => await _factory.DisposeAsync();

    [TestMethod]
    public async Task Upload_Download_RoundTrip()
    {
        var payload = Encoding.UTF8.GetBytes("Hello NexSync");
        var (upRes, upDoc) = await _client.UploadAsync(payload, "hello.txt", "text/plain");
        Assert.AreEqual(HttpStatusCode.Created, upRes.StatusCode);
        Assert.IsNotNull(upDoc);
        var id = upDoc.RootElement.GetProperty("id").GetGuid();

        var downRes = await _client.DownloadRawAsync(id);
        Assert.AreEqual(HttpStatusCode.OK, downRes.StatusCode);
        var bytes = await downRes.Content.ReadAsByteArrayAsync();
        CollectionAssert.AreEqual(payload, bytes);
    }

    [TestMethod]
    public async Task Upload_DuplicateName_Returns409()
    {
        await _client.UploadAsync([1, 2, 3], "dup.bin", "application/octet-stream");
        var (res, _) = await _client.UploadAsync([4, 5, 6], "DUP.bin", "application/octet-stream");
        Assert.AreEqual(HttpStatusCode.Conflict, res.StatusCode);
    }

    [TestMethod]
    public async Task Upload_IdenticalContent_ReusesStorage()
    {
        var payload = Encoding.UTF8.GetBytes("same content here");
        var (_, doc1) = await _client.UploadAsync(payload, "a.txt", "text/plain");
        var (_, doc2) = await _client.UploadAsync(payload, "b.txt", "text/plain");
        Assert.IsNotNull(doc1);
        Assert.IsNotNull(doc2);
        Assert.AreEqual(
            doc1.RootElement.GetProperty("hash").GetString(),
            doc2.RootElement.GetProperty("hash").GetString());
        var id1 = doc1.RootElement.GetProperty("id").GetGuid();
        var del = await _client.DeleteAsync($"/api/files/{id1}");
        Assert.AreEqual(HttpStatusCode.NoContent, del.StatusCode);
        var id2 = doc2.RootElement.GetProperty("id").GetGuid();
        var down = await _client.DownloadRawAsync(id2);
        Assert.AreEqual(HttpStatusCode.OK, down.StatusCode);
    }

    [TestMethod]
    public async Task Rename_Conflict_Returns409()
    {
        await _client.UploadAsync([1], "one.txt", "text/plain");
        var (_, doc2) = await _client.UploadAsync([2], "two.txt", "text/plain");
        var id2 = doc2!.RootElement.GetProperty("id").GetGuid();
        var (res, _) = await _client.PutJsonAsync($"/api/files/{id2}", new { name = "ONE.txt", folderId = (Guid?)null });
        Assert.AreEqual(HttpStatusCode.Conflict, res.StatusCode);
    }
}



