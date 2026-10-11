using System.Net;
using System.Net.Http.Json;
using Microsoft.Extensions.DependencyInjection;

namespace NexSync.Tests;

[TestClass]
public sealed class WebDeviceFlowTests
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
    public async Task WebFlow_RegisterLoginDeviceMutate_OriginAttributed()
    {
        var (regRes, _) = await _client.RegisterAsync("web@nexsync.dev", "password123", "Web User");
        Assert.AreEqual(HttpStatusCode.OK, regRes.StatusCode);

        var devReq = new HttpRequestMessage(HttpMethod.Post, "/api/devices")
        {
            Content = JsonContent.Create(new { name = "Chrome - Windows", platform = "Web" })
        };
        devReq.Headers.Add("X-Operation-Id", Guid.NewGuid().ToString());
        var devRes = await _client.SendRawAsync(devReq);
        Assert.AreEqual(HttpStatusCode.Created, devRes.StatusCode);
        var devDoc = await ApiClient.ParseAsync(devRes);
        var deviceId = devDoc!.RootElement.GetProperty("deviceId").GetGuid();
        _client.DeviceId = deviceId;

        var (fres, fdoc) = await _client.PostJsonAsync("/api/folders", new { name = "WebFolder", parentFolderId = (Guid?)null });
        Assert.AreEqual(HttpStatusCode.Created, fres.StatusCode);

        var (ures, _) = await _client.UploadAsync(System.Text.Encoding.UTF8.GetBytes("web content"), "web.txt", "text/plain");
        Assert.AreEqual(HttpStatusCode.Created, ures.StatusCode);

        var (pres, pdoc) = await _client.GetAsync("/api/sync/pull?since=0&until=100&limit=500");
        Assert.AreEqual(HttpStatusCode.OK, pres.StatusCode);
        var changes = pdoc!.RootElement.GetProperty("changes").EnumerateArray().ToList();
        Assert.AreEqual(2, changes.Count);
        Assert.IsTrue(changes.All(c => c.GetProperty("originDeviceId").GetGuid() == deviceId));
    }

    [TestMethod]
    public async Task WebFlow_SameBrowserNewUser_GetsOwnDevice()
    {
        await _client.RegisterAsync("web-a@nexsync.dev", "password123", "User A");
        var reqA = new HttpRequestMessage(HttpMethod.Post, "/api/devices")
        {
            Content = JsonContent.Create(new { name = "Shared Browser", platform = "Web" })
        };
        reqA.Headers.Add("X-Operation-Id", Guid.NewGuid().ToString());
        var resA = await _client.SendRawAsync(reqA);
        var docA = await ApiClient.ParseAsync(resA);
        var deviceA = docA!.RootElement.GetProperty("deviceId").GetGuid();

        var other = new ApiClient(_factory.CreateClient());
        await other.RegisterAsync("web-b@nexsync.dev", "password123", "User B");
        other.DeviceId = deviceA;
        var (res, _) = await other.GetAsync("/api/sync/pull?since=0&until=10&limit=10");
        Assert.AreEqual(HttpStatusCode.Forbidden, res.StatusCode);
    }

    [TestMethod]
    public async Task WebFlow_RevokedDevice_BlockedWithClearStatus()
    {
        await _client.RegisterAsync("web-r@nexsync.dev", "password123", "Revoked User");
        var req = new HttpRequestMessage(HttpMethod.Post, "/api/devices")
        {
            Content = JsonContent.Create(new { name = "Doomed Browser", platform = "Web" })
        };
        req.Headers.Add("X-Operation-Id", Guid.NewGuid().ToString());
        var res = await _client.SendRawAsync(req);
        var doc = await ApiClient.ParseAsync(res);
        var deviceId = doc!.RootElement.GetProperty("deviceId").GetGuid();
        _client.DeviceId = deviceId;

        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<NexSync.Infrastructure.Data.AppDbContext>();
            var dev = await db.Devices.FindAsync(deviceId);
            dev!.RevokedAt = DateTime.UtcNow;
            await db.SaveChangesAsync();
        }

        var (mres, _) = await _client.PostJsonAsync("/api/folders", new { name = "Nope", parentFolderId = (Guid?)null });
        Assert.AreEqual(HttpStatusCode.Forbidden, mres.StatusCode);
    }
}
