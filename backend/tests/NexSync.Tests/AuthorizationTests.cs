using System.Net;

namespace NexSync.Tests;

[TestClass]
public sealed class AuthorizationTests 
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
        await _alice.RegisterAsync("alice2@nexsync.dev", "password123", "Alice");
        await _bob.RegisterAsync("bob2@nexsync.dev", "password123", "Bob");
    }

    [TestCleanup]
    public async Task DisposeAsync() => await _factory.DisposeAsync();

    [TestMethod]
    public async Task Bob_CannotRead_AlicesFolder()
    {
        var (_, doc) = await _alice.PostJsonAsync("/api/folders", new { name = "Secret", parentFolderId = (Guid?)null });
        var id = doc!.RootElement.GetProperty("id").GetGuid();
        var (res, _) = await _bob.GetAsync($"/api/folders/{id}");
        Assert.AreEqual(HttpStatusCode.Forbidden, res.StatusCode);
    }

    [TestMethod]
    public async Task Bob_CannotDownload_AlicesFile()
    {
        var (_, doc) = await _alice.UploadAsync([9, 9, 9], "secret.bin", "application/octet-stream");
        var id = doc!.RootElement.GetProperty("id").GetGuid();
        var res = await _bob.DownloadRawAsync(id);
        Assert.AreEqual(HttpStatusCode.Forbidden, res.StatusCode);
    }

    [TestMethod]
    public async Task Bob_CannotMoveInto_AlicesFolder()
    {
        var (_, docF) = await _alice.PostJsonAsync("/api/folders", new { name = "AliceFolder", parentFolderId = (Guid?)null });
        var aliceFolder = docF!.RootElement.GetProperty("id").GetGuid();
        var (_, docB) = await _bob.PostJsonAsync("/api/folders", new { name = "BobFolder", parentFolderId = (Guid?)null });
        var bobFolder = docB!.RootElement.GetProperty("id").GetGuid();
        var (res, _) = await _bob.PutJsonAsync($"/api/folders/{bobFolder}", new { name = "BobFolder", parentFolderId = aliceFolder });
        Assert.AreEqual(HttpStatusCode.Forbidden, res.StatusCode);
    }

    [TestMethod]
    public async Task Bob_CannotDelete_AlicesFolder()
    {
        var (_, doc) = await _alice.PostJsonAsync("/api/folders", new { name = "Victim", parentFolderId = (Guid?)null });
        var id = doc!.RootElement.GetProperty("id").GetGuid();
        var res = await _bob.DeleteAsync($"/api/folders/{id}");
        Assert.AreEqual(HttpStatusCode.Forbidden, res.StatusCode);
    }

    [TestMethod]
    public async Task Anonymous_CannotAccess_Folders()
    {
        var anon = new ApiClient(_factory.CreateClient());
        var (res, _) = await anon.GetAsync("/api/folders");
        Assert.AreEqual(HttpStatusCode.Unauthorized, res.StatusCode);
    }
}



