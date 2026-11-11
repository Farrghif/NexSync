using System.Net;
using System.Net.Http.Json;

namespace NexSync.Tests;

/// <summary>
/// Guards for the three backend boot defects (PHASE_J_AUTONOMOUS_PLAN Task 2):
/// missing IDistributedCache, UseSession/CsrfMiddleware order, rate limiter always allowing.
/// </summary>
[TestClass]
public sealed class BackendBootTests
{
    private readonly NexSyncApiFactory _factory = new();

    [TestInitialize]
    public async Task InitializeAsync() => await _factory.InitializeAsync();

    [TestCleanup]
    public async Task DisposeAsync() => await _factory.DisposeAsync();

    [TestMethod]
    public async Task Host_Starts_And_TrivialRequest_IsNot500()
    {
        var client = _factory.CreateClient();

        var res = await client.GetAsync("/");

        Assert.AreNotEqual(HttpStatusCode.InternalServerError, res.StatusCode);
        Assert.IsTrue(res.Headers.Contains("X-CSRF-Token"),
            "CsrfMiddleware must run with a working session and emit the CSRF token header");
    }

    [TestMethod]
    public async Task Login_SixthRequestWithinWindow_Returns429()
    {
        var client = _factory.CreateClient();

        HttpResponseMessage? last = null;
        for (var i = 0; i < 6; i++)
        {
            var req = new HttpRequestMessage(HttpMethod.Post, "/api/auth/login")
            {
                Content = JsonContent.Create(new { email = "nobody@nexsync.dev", password = "wrong-password" })
            };
            // Rate limiter keys on this header, so the window is deterministic under TestServer.
            req.Headers.Add("X-Forwarded-For", "203.0.113.9");
            last = await client.SendAsync(req);
        }

        // 5 requests allowed per 60s window; request N+1 must be rejected.
        Assert.AreEqual(HttpStatusCode.TooManyRequests, last!.StatusCode);
    }
}
