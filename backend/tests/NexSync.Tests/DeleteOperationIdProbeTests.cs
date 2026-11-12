using System.Net;
using System.Net.Http.Headers;
using System.Text;

namespace NexSync.Tests;

// TEMPORARY probe: replicates the desktop's exact delete request (X-Device-Id +
// X-Operation-Id) which no existing test covers. Console logging is enabled so the
// unhandled exception behind the live backend's HTTP 500 appears in test output.
[TestClass]
public sealed class DeleteOperationIdProbeTests
{
    private readonly NexSyncApiFactory _factory = new();
    private ApiClient _client = null!;

    [TestInitialize]
    public async Task InitializeAsync()
    {
        await _factory.InitializeAsync();
        _client = new ApiClient(_factory.CreateClient());
        await _client.RegisterAsync("delprobe@nexsync.dev", "Password123!", "Delete Probe");

        var devReq = new HttpRequestMessage(HttpMethod.Post, "/api/devices")
        {
            Content = System.Net.Http.Json.JsonContent.Create(new { name = "Probe Device", platform = "Desktop" })
        };
        devReq.Headers.Add("X-Operation-Id", Guid.NewGuid().ToString());
        var devRes = await _client.SendRawAsync(devReq);
        var devDoc = await ApiClient.ParseAsync(devRes);
        Assert.AreEqual(HttpStatusCode.Created, devRes.StatusCode, "device registration must succeed");
        _client.DeviceId = devDoc!.RootElement.GetProperty("deviceId").GetGuid();
    }

    [TestCleanup]
    public async Task DisposeAsync() => await _factory.DisposeAsync();

    [TestMethod]
    public async Task DesktopStyleDelete_WithOperationId_Returns204()
    {
        // 1. Upload exactly like the desktop: X-Device-Id (ApiClient) + X-Operation-Id.
        byte[] payload = Encoding.UTF8.GetBytes("probe delete " + Guid.NewGuid());
        using var form = new MultipartFormDataContent();
        var bytes = new ByteArrayContent(payload);
        bytes.Headers.ContentType = new MediaTypeHeaderValue("text/plain");
        form.Add(bytes, "file", "probe-del.txt");
        var upReq = new HttpRequestMessage(HttpMethod.Post, "/api/files/upload") { Content = form };
        upReq.Headers.Add("X-Operation-Id", Guid.NewGuid().ToString());
        var upRes = await _client.SendRawAsync(upReq);
        var upDoc = await ApiClient.ParseAsync(upRes);
        Assert.AreEqual(HttpStatusCode.Created, upRes.StatusCode, "upload must succeed: " + await upRes.Content.ReadAsStringAsync());
        var fileId = upDoc!.RootElement.GetProperty("id").GetGuid();

        // 2. DELETE exactly like the desktop: X-Device-Id + X-Operation-Id.
        var delReq = new HttpRequestMessage(HttpMethod.Delete, $"/api/files/{fileId}");
        delReq.Headers.Add("X-Operation-Id", Guid.NewGuid().ToString());
        var delRes = await _client.SendRawAsync(delReq);
        var body = await delRes.Content.ReadAsStringAsync();
        Console.WriteLine($"[probe] DELETE /api/files/{fileId} -> {(int)delRes.StatusCode} {delRes.StatusCode}; body={body}");
        Assert.AreEqual(HttpStatusCode.NoContent, delRes.StatusCode,
            "desktop-style delete must return 204; body: " + body);
    }
}
