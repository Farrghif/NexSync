using System.Net;
using System.Net.Http.Json;

namespace NexSync.Tests;

[TestClass]
public sealed class DeviceTests
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
        await _alice.RegisterAsync("dev-alice@nexsync.dev", "password123", "Alice");
        await _bob.RegisterAsync("dev-bob@nexsync.dev", "password123", "Bob");
    }

    [TestCleanup]
    public async Task DisposeAsync() => await _factory.DisposeAsync();

    private async Task<(HttpResponseMessage, System.Text.Json.JsonDocument?)> RegisterDeviceAsync(ApiClient client, object body, Guid? operationId)
    {
        var req = new HttpRequestMessage(HttpMethod.Post, "/api/devices") { Content = JsonContent.Create(body) };
        if (operationId.HasValue) req.Headers.Add("X-Operation-Id", operationId.Value.ToString());
        var res = await client.SendRawAsync(req);
        return (res, await ApiClient.ParseAsync(res));
    }

    [TestMethod]
    public async Task Register_ThenList_ShowsActive()
    {
        var opId = Guid.NewGuid();
        var (res, doc) = await RegisterDeviceAsync(_alice, new { name = " Alice Laptop ", platform = "Windows" }, opId);
        Assert.AreEqual(HttpStatusCode.Created, res.StatusCode);
        Assert.AreEqual("Alice Laptop", doc!.RootElement.GetProperty("name").GetString());

        var (listRes, listDoc) = await _alice.GetAsync("/api/devices");
        Assert.AreEqual(HttpStatusCode.OK, listRes.StatusCode);
        var items = listDoc!.RootElement.EnumerateArray().ToList();
        Assert.AreEqual(1, items.Count);
        Assert.AreEqual("ACTIVE", items[0].GetProperty("status").GetString());
    }

    [TestMethod]
    public async Task Register_SameOperationId_ReplaysSingleRow()
    {
        var opId = Guid.NewGuid();
        var body = new { name = "Phone", platform = "Android" };
        var (r1, d1) = await RegisterDeviceAsync(_alice, body, opId);
        var (r2, d2) = await RegisterDeviceAsync(_alice, body, opId);
        Assert.AreEqual(HttpStatusCode.Created, r1.StatusCode);
        Assert.AreEqual(HttpStatusCode.Created, r2.StatusCode);
        Assert.AreEqual(
            d1!.RootElement.GetProperty("deviceId").GetGuid(),
            d2!.RootElement.GetProperty("deviceId").GetGuid());

        var (_, listDoc) = await _alice.GetAsync("/api/devices");
        Assert.AreEqual(1, listDoc!.RootElement.GetArrayLength());
    }

    [TestMethod]
    public async Task Register_SameOperationId_DifferentBody_Returns400()
    {
        var opId = Guid.NewGuid();
        var (r1, _) = await RegisterDeviceAsync(_alice, new { name = "Phone", platform = "Android" }, opId);
        Assert.AreEqual(HttpStatusCode.Created, r1.StatusCode);
        var (r2, _) = await RegisterDeviceAsync(_alice, new { name = "Other", platform = "Android" }, opId);
        Assert.AreEqual(HttpStatusCode.BadRequest, r2.StatusCode);
    }

    [TestMethod]
    public async Task Register_InvalidNameOrPlatform_Returns400()
    {
        var (r1, _) = await RegisterDeviceAsync(_alice, new { name = "   ", platform = "Windows" }, Guid.NewGuid());
        Assert.AreEqual(HttpStatusCode.BadRequest, r1.StatusCode);
        var (r2, _) = await RegisterDeviceAsync(_alice, new { name = new string('x', 101), platform = "Windows" }, Guid.NewGuid());
        Assert.AreEqual(HttpStatusCode.BadRequest, r2.StatusCode);
        var (r3, _) = await RegisterDeviceAsync(_alice, new { name = "PC", platform = "Linux" }, Guid.NewGuid());
        Assert.AreEqual(HttpStatusCode.BadRequest, r3.StatusCode);
        var (r4, _) = await RegisterDeviceAsync(_alice, new { name = "PC", platform = "Windows" }, null);
        Assert.AreEqual(HttpStatusCode.BadRequest, r4.StatusCode);
    }

    [TestMethod]
    public async Task Delete_SetsRevoked_StillListed_Idempotent()
    {
        var (_, doc) = await RegisterDeviceAsync(_alice, new { name = "Old PC", platform = "Web" }, Guid.NewGuid());
        var id = doc!.RootElement.GetProperty("deviceId").GetGuid();

        var d1 = await _alice.DeleteAsync($"/api/devices/{id}");
        Assert.AreEqual(HttpStatusCode.NoContent, d1.StatusCode);
        var d2 = await _alice.DeleteAsync($"/api/devices/{id}");
        Assert.AreEqual(HttpStatusCode.NoContent, d2.StatusCode);

        var (listRes, listDoc) = await _alice.GetAsync("/api/devices");
        Assert.AreEqual(HttpStatusCode.OK, listRes.StatusCode);
        var item = listDoc!.RootElement.EnumerateArray().Single(d => d.GetProperty("deviceId").GetGuid() == id);
        Assert.AreEqual("REVOKED", item.GetProperty("status").GetString());
    }

    [TestMethod]
    public async Task OtherUsersDevice_IsInvisible()
    {
        var (_, doc) = await RegisterDeviceAsync(_alice, new { name = "Alice Secret", platform = "Windows" }, Guid.NewGuid());
        var id = doc!.RootElement.GetProperty("deviceId").GetGuid();

        var (_, bobList) = await _bob.GetAsync("/api/devices");
        Assert.IsFalse(bobList!.RootElement.EnumerateArray().Any(d => d.GetProperty("deviceId").GetGuid() == id));
        var bobDel = await _bob.DeleteAsync($"/api/devices/{id}");
        Assert.AreEqual(HttpStatusCode.NotFound, bobDel.StatusCode);
    }

    [TestMethod]
    public async Task Anonymous_CannotAccess_Devices()
    {
        var anon = new ApiClient(_factory.CreateClient());
        var (res, _) = await anon.GetAsync("/api/devices");
        Assert.AreEqual(HttpStatusCode.Unauthorized, res.StatusCode);
    }
}
