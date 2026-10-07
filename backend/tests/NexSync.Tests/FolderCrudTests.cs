using System.Net;

namespace NexSync.Tests;

[TestClass]
public sealed class FolderCrudTests 
{
    private readonly NexSyncApiFactory _factory = new();
    private ApiClient _client = null!;

    [TestInitialize]
    public async Task InitializeAsync()
    {
        await _factory.InitializeAsync();
        _client = new ApiClient(_factory.CreateClient());
        await _client.RegisterAsync("folder@nexsync.dev", "password123", "Folder User");
    }

    [TestCleanup]
    public async Task DisposeAsync() => await _factory.DisposeAsync();

    [TestMethod]
    public async Task CreateFolder_Returns201()
    {
        var (res, doc) = await _client.PostJsonAsync("/api/folders", new { name = "Projects", parentFolderId = (Guid?)null });
        Assert.AreEqual(HttpStatusCode.Created, res.StatusCode);
        Assert.IsNotNull(doc);
        Assert.AreEqual("Projects", doc.RootElement.GetProperty("name").GetString());
    }

    [TestMethod]
    public async Task CreateFolder_DuplicateName_Returns409()
    {
        await _client.PostJsonAsync("/api/folders", new { name = "Docs", parentFolderId = (Guid?)null });
        var (res, _) = await _client.PostJsonAsync("/api/folders", new { name = "docs", parentFolderId = (Guid?)null });
        Assert.AreEqual(HttpStatusCode.Conflict, res.StatusCode);
    }

    [TestMethod]
    public async Task CreateFolder_WindowsReservedName_Returns400()
    {
        var (res, _) = await _client.PostJsonAsync("/api/folders", new { name = "CON", parentFolderId = (Guid?)null });
        Assert.AreEqual(HttpStatusCode.BadRequest, res.StatusCode);
    }

    [TestMethod]
    public async Task CreateFolder_ForbiddenChars_Returns400()
    {
        var (res, _) = await _client.PostJsonAsync("/api/folders", new { name = "bad?name", parentFolderId = (Guid?)null });
        Assert.AreEqual(HttpStatusCode.BadRequest, res.StatusCode);
    }

    [TestMethod]
    public async Task MoveFolder_IntoItself_Returns409()
    {
        var (_, doc) = await _client.PostJsonAsync("/api/folders", new { name = "A", parentFolderId = (Guid?)null });
        var id = doc!.RootElement.GetProperty("id").GetGuid();
        var (res, _) = await _client.PutJsonAsync($"/api/folders/{id}", new { name = "A", parentFolderId = id });
        Assert.AreEqual(HttpStatusCode.Conflict, res.StatusCode);
    }

    [TestMethod]
    public async Task MoveFolder_IntoDescendant_Returns409()
    {
        var (_, docA) = await _client.PostJsonAsync("/api/folders", new { name = "A2", parentFolderId = (Guid?)null });
        var a = docA!.RootElement.GetProperty("id").GetGuid();
        var (_, docB) = await _client.PostJsonAsync("/api/folders", new { name = "B2", parentFolderId = a });
        var b = docB!.RootElement.GetProperty("id").GetGuid();
        var (_, docC) = await _client.PostJsonAsync("/api/folders", new { name = "C2", parentFolderId = b });
        var c = docC!.RootElement.GetProperty("id").GetGuid();
        var (res, _) = await _client.PutJsonAsync($"/api/folders/{a}", new { name = "A2", parentFolderId = c });
        Assert.AreEqual(HttpStatusCode.Conflict, res.StatusCode);
    }

    [TestMethod]
    public async Task MoveFolder_ToSibling_Succeeds()
    {
        var (_, docA) = await _client.PostJsonAsync("/api/folders", new { name = "SA", parentFolderId = (Guid?)null });
        var a = docA!.RootElement.GetProperty("id").GetGuid();
        var (_, docB) = await _client.PostJsonAsync("/api/folders", new { name = "SB", parentFolderId = (Guid?)null });
        var b = docB!.RootElement.GetProperty("id").GetGuid();
        var (_, docC) = await _client.PostJsonAsync("/api/folders", new { name = "SC", parentFolderId = a });
        var c = docC!.RootElement.GetProperty("id").GetGuid();
        var (res, _) = await _client.PutJsonAsync($"/api/folders/{c}", new { name = "SC", parentFolderId = b });
        Assert.AreEqual(HttpStatusCode.OK, res.StatusCode);
    }

    [TestMethod]
    public async Task GetContents_Paginates()
    {
        for (int i = 0; i < 5; i++)
            await _client.PostJsonAsync("/api/folders", new { name = $"Pag{i}", parentFolderId = (Guid?)null });
        var (res, doc) = await _client.GetAsync("/api/folders?page=1&pageSize=2");
        Assert.AreEqual(HttpStatusCode.OK, res.StatusCode);
        Assert.IsNotNull(doc);
        Assert.AreEqual(2, doc.RootElement.GetProperty("folders").GetArrayLength());
        Assert.IsTrue(doc.RootElement.GetProperty("totalCount").GetInt32() >= 5);
    }
}



