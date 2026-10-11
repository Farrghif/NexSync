using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NexSync.Desktop.Auth;

namespace NexSync.Desktop.Tests;

[TestClass]
public sealed class SecureTokenStoreTests
{
    private string _dir = null!;

    [TestInitialize]
    public void Setup() => _dir = Path.Combine(Path.GetTempPath(), "nexsync-desktop-test", Guid.NewGuid().ToString("N"));

    [TestCleanup]
    public void Teardown()
    {
        if (Directory.Exists(_dir))
            Directory.Delete(_dir, recursive: true);
    }

    [TestMethod]
    public void SaveLoad_RoundTripsToken()
    {
        var store = new SecureTokenStore(Path.Combine(_dir, "refresh-token.dat"));
        store.Save("super-secret-refresh-token");
        Assert.AreEqual("super-secret-refresh-token", store.Load());
    }

    [TestMethod]
    public void SavedToken_IsNotPlaintextOnDisk()
    {
        var path = Path.Combine(_dir, "refresh-token.dat");
        var store = new SecureTokenStore(path);
        store.Save("super-secret-refresh-token");
        var raw = File.ReadAllText(path);
        Assert.IsFalse(raw.Contains("super-secret-refresh-token"));
    }

    [TestMethod]
    public void Load_MissingFile_ReturnsNull()
    {
        var store = new SecureTokenStore(Path.Combine(_dir, "refresh-token.dat"));
        Assert.IsNull(store.Load());
    }

    [TestMethod]
    public void Save_CrashSimulatedByLeftoverTemp_KeepsPreviousToken()
    {
        var path = Path.Combine(_dir, "refresh-token.dat");
        var store = new SecureTokenStore(path);
        store.Save("original-token");
        File.WriteAllBytes(path + ".tmp-deadbeef", Encoding.UTF8.GetBytes("partial garbage"));
        Assert.AreEqual("original-token", store.Load());
        store.Save("rotated-token");
        Assert.AreEqual("rotated-token", store.Load());
    }

    [TestMethod]
    public void Clear_RemovesStoredToken()
    {
        var store = new SecureTokenStore(Path.Combine(_dir, "refresh-token.dat"));
        store.Save("some-token");
        store.Clear();
        Assert.IsNull(store.Load());
    }
}

[TestClass]
public sealed class DesktopAuthServiceTests
{
    private string _dir = null!;

    [TestInitialize]
    public void Setup() => _dir = Path.Combine(Path.GetTempPath(), "nexsync-desktop-test", Guid.NewGuid().ToString("N"));

    [TestCleanup]
    public void Teardown()
    {
        if (Directory.Exists(_dir))
            Directory.Delete(_dir, recursive: true);
    }

    private static string TempDir() =>
        Path.Combine(Path.GetTempPath(), "nexsync-desktop-test", Guid.NewGuid().ToString("N"));

    [TestMethod]
    public async Task Login_Success_SetsUserAndAccessTokenInMemoryOnly()
    {
        var handler = new StubHandler(req =>
        {
            Assert.AreEqual("/api/auth/login", req.RequestUri!.AbsolutePath);
            return JsonResponse(HttpStatusCode.OK, new
            {
                accessToken = "jwt-abc",
                user = new { id = Guid.NewGuid(), email = "u@x.dev", fullName = "U" }
            });
        });
        var dir = TempDir();
        using var auth = new DesktopAuthService("http://localhost:5000", dir, handler);
        var user = await auth.LoginAsync("u@x.dev", "password123");
        Assert.AreEqual("u@x.dev", user.Email);
        Assert.IsTrue(auth.IsSignedIn);
        foreach (var f in Directory.GetFiles(dir, "*", SearchOption.AllDirectories))
            Assert.IsFalse(File.ReadAllText(f).Contains("jwt-abc"), $"Access token persisted in {f}");
    }

    [TestMethod]
    public async Task Login_WrongPassword_ThrowsUnauthorized()
    {
        var handler = new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.Unauthorized));
        using var auth = new DesktopAuthService("http://localhost:5000", TempDir(), handler);
        await Assert.ThrowsExactlyAsync<UnauthorizedException>(() => auth.LoginAsync("u@x.dev", "wrong"));
        Assert.IsFalse(auth.IsSignedIn);
    }

    [TestMethod]
    public async Task SendWithAuth_401Once_RefreshesAndRetriesWithSameOperationId()
    {
        var seenOperationIds = new System.Collections.Generic.List<string?>();
        var calls = 0;
        var handler = new StubHandler(req =>
        {
            calls++;
            if (req.RequestUri!.AbsolutePath == "api/auth/refresh" || req.RequestUri!.AbsolutePath == "/api/auth/refresh")
                return JsonResponse(HttpStatusCode.OK, new { accessToken = "jwt-new" });
            if (calls == 1)
                return new HttpResponseMessage(HttpStatusCode.Unauthorized);
            seenOperationIds.Add(req.Headers.TryGetValues("X-Operation-Id", out var v) ? string.Join(",", v) : null);
            return JsonResponse(HttpStatusCode.OK, new { ok = true });
        });
        using var auth = new DesktopAuthService("http://localhost:5000", TempDir(), handler);
        using var req = new HttpRequestMessage(HttpMethod.Post, "api/folders");
        req.Content = new StringContent("{\"name\":\"F\"}", Encoding.UTF8, "application/json");
        req.Headers.Add("X-Operation-Id", "11111111-1111-1111-1111-111111111111");
        using var res = await auth.SendWithAuthAsync(req, allowRefresh: true);
        Assert.AreEqual(HttpStatusCode.OK, res.StatusCode);
        Assert.AreEqual(1, seenOperationIds.Count);
        Assert.AreEqual("11111111-1111-1111-1111-111111111111", seenOperationIds[0]);
    }

    [TestMethod]
    public async Task SendWithAuth_RefreshFails_ReturnsOriginal401()
    {
        var handler = new StubHandler(req =>
            req.RequestUri!.AbsolutePath.EndsWith("/api/auth/refresh")
                ? new HttpResponseMessage(HttpStatusCode.Unauthorized)
                : new HttpResponseMessage(HttpStatusCode.Unauthorized));
        using var auth = new DesktopAuthService("http://localhost:5000", TempDir(), handler);
        using var req = new HttpRequestMessage(HttpMethod.Get, "api/sync/cursor");
        using var res = await auth.SendWithAuthAsync(req, allowRefresh: true);
        Assert.AreEqual(HttpStatusCode.Unauthorized, res.StatusCode);
    }

    [TestMethod]
    public async Task EnsureDeviceRegistered_SendsNoDeviceId_ReusesOperationIdOnRetry()
    {
        var deviceRequests = new System.Collections.Generic.List<(string? DeviceId, string? OpId)>();
        var deviceId = Guid.NewGuid();
        var handler = new StubHandler(req =>
        {
            deviceRequests.Add((
                req.Headers.TryGetValues("X-Device-Id", out var d) ? string.Join(",", d) : null,
                req.Headers.TryGetValues("X-Operation-Id", out var o) ? string.Join(",", o) : null));
            return JsonResponse(HttpStatusCode.Created, new
            {
                deviceId,
                name = "PC",
                platform = "Windows",
                status = "ACTIVE",
                createdAt = DateTime.UtcNow,
                lastSeenAt = DateTime.UtcNow,
                revokedAt = (DateTime?)null
            });
        });
        var dir = TempDir();
        using (var auth = new DesktopAuthService("http://localhost:5000", dir, handler))
        {
            var first = await auth.EnsureDeviceRegisteredAsync("PC");
            var second = await auth.EnsureDeviceRegisteredAsync("PC");
            Assert.AreEqual(deviceId, first);
            Assert.AreEqual(deviceId, second);
        }
        Assert.AreEqual(1, deviceRequests.Count);
        Assert.IsNull(deviceRequests[0].DeviceId);
        Assert.IsNotNull(deviceRequests[0].OpId);

        using (var auth2 = new DesktopAuthService("http://localhost:5000", dir, handler))
        {
            Assert.AreEqual(deviceId.ToString(), auth2.DeviceId);
        }
    }

    [TestMethod]
    public async Task Logout_ClearsSessionLocally()
    {
        var handler = new StubHandler(req =>
            req.RequestUri!.AbsolutePath.EndsWith("/api/auth/logout")
                ? new HttpResponseMessage(HttpStatusCode.NoContent)
                : JsonResponse(HttpStatusCode.OK, new
                {
                    accessToken = "jwt-abc",
                    user = new { id = Guid.NewGuid(), email = "u@x.dev", fullName = "U" }
                }));
        using var auth = new DesktopAuthService("http://localhost:5000", TempDir(), handler);
        await auth.LoginAsync("u@x.dev", "password123");
        Assert.IsTrue(auth.IsSignedIn);
        await auth.LogoutAsync();
        Assert.IsFalse(auth.IsSignedIn);
        Assert.IsNull(auth.CurrentUser);
    }

    private static HttpResponseMessage JsonResponse(HttpStatusCode status, object payload)
    {
        var res = new HttpResponseMessage(status);
        res.Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");
        return res;
    }

    private sealed class StubHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => Task.FromResult(respond(request));
    }
}
