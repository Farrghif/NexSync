namespace NexSync.Tests;

[TestClass]
public sealed class AuthFlowTests 
{
    private readonly NexSyncApiFactory _factory = new();
    private ApiClient _client = null!;

    [TestInitialize]
    public async Task InitializeAsync()
    {
        await _factory.InitializeAsync();
        _client = new ApiClient(_factory.CreateClient());
    }

    [TestCleanup]
    public async Task DisposeAsync() => await _factory.DisposeAsync();

    [TestMethod]
    public async Task Register_CreatesUser_ReturnsTokens()
    {
        var (res, doc) = await _client.RegisterAsync("alice@nexsync.dev", "password123", "Alice");
        Assert.AreEqual(System.Net.HttpStatusCode.OK, res.StatusCode);
        Assert.IsNotNull(doc);
        Assert.IsNotNull(_client.AccessToken);
        Assert.IsNotNull(_client.RefreshCookie);
    }

    [TestMethod]
    public async Task Register_DuplicateEmail_Returns409()
    {
        await _client.RegisterAsync("bob@nexsync.dev", "password123", "Bob");
        var other = new ApiClient(_factory.CreateClient());
        var (res, _) = await other.RegisterAsync("bob@nexsync.dev", "password123", "Bob2");
        Assert.AreEqual(System.Net.HttpStatusCode.Conflict, res.StatusCode);
    }

    [TestMethod]
    public async Task Register_InvalidEmail_Returns400()
    {
        var (res, _) = await _client.RegisterAsync("not-an-email", "password123", "X");
        Assert.AreEqual(System.Net.HttpStatusCode.BadRequest, res.StatusCode);
    }

    [TestMethod]
    public async Task Login_WrongPassword_Returns401()
    {
        await _client.RegisterAsync("carol@nexsync.dev", "password123", "Carol");
        var other = new ApiClient(_factory.CreateClient());
        var (res, _) = await other.LoginAsync("carol@nexsync.dev", "wrongpassword");
        Assert.AreEqual(System.Net.HttpStatusCode.Unauthorized, res.StatusCode);
    }

    [TestMethod]
    public async Task Refresh_RotatesToken_OldTokenInvalid()
    {
        await _client.RegisterAsync("dave@nexsync.dev", "password123", "Dave");
        var oldCookie = _client.RefreshCookie;
        Assert.IsNotNull(oldCookie);
        var (res, _) = await _client.RefreshAsync();
        Assert.AreEqual(System.Net.HttpStatusCode.OK, res.StatusCode);
        Assert.AreNotEqual(oldCookie, _client.RefreshCookie);

        var replay = new ApiClient(_factory.CreateClient()) { RefreshCookie = oldCookie };
        var (res2, _) = await replay.RefreshAsync();
        Assert.AreEqual(System.Net.HttpStatusCode.Unauthorized, res2.StatusCode);
    }

    [TestMethod]
    public async Task Refresh_WithoutCsrfHeader_Returns400()
    {
        await _client.RegisterAsync("erin@nexsync.dev", "password123", "Erin");
        var req = new HttpRequestMessage(HttpMethod.Post, "/api/auth/refresh");
        req.Headers.Add("Cookie", $"refreshToken={_client.RefreshCookie}");
        var res = await _factory.CreateClient().SendAsync(req);
        Assert.AreEqual(System.Net.HttpStatusCode.BadRequest, res.StatusCode);
    }

    [TestMethod]
    public async Task Logout_RevokesToken_RefreshFails()
    {
        await _client.RegisterAsync("frank@nexsync.dev", "password123", "Frank");
        var res = await _client.LogoutAsync();
        Assert.AreEqual(System.Net.HttpStatusCode.NoContent, res.StatusCode);
        var (res2, _) = await _client.RefreshAsync();
        Assert.AreEqual(System.Net.HttpStatusCode.Unauthorized, res2.StatusCode);
    }
}



