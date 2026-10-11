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
        var deviceId = Guid.NewGuid();
        var handler = new StubHandler(req =>
        {
            if (req.RequestUri!.AbsolutePath.EndsWith("/api/auth/logout"))
                return new HttpResponseMessage(HttpStatusCode.NoContent);
            if (req.RequestUri!.AbsolutePath.EndsWith("/api/auth/refresh"))
            {
                var sent = req.Headers.TryGetValues("Cookie", out var v) ? string.Join(";", v) : string.Empty;
                if (sent.Contains("refreshToken="))
                    return JsonResponse(HttpStatusCode.OK, new { accessToken = "jwt-after-logout" });
                return new HttpResponseMessage(HttpStatusCode.Unauthorized);
            }
            if (req.RequestUri!.AbsolutePath.EndsWith("/api/devices"))
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
            var loginRes = JsonResponse(HttpStatusCode.OK, new
            {
                accessToken = "jwt-abc",
                user = new { id = Guid.NewGuid(), email = "u@x.dev", fullName = "U" }
            });
            loginRes.Headers.Add("Set-Cookie", "refreshToken=rt-logout-test; Path=/api/auth; HttpOnly");
            return loginRes;
        });
        var dir = TempDir();
        using var auth = new DesktopAuthService("http://localhost:5000", dir, handler);
        await auth.LoginAsync("u@x.dev", "password123");
        Assert.IsTrue(auth.IsSignedIn);
        Assert.IsTrue(File.Exists(Path.Combine(dir, "refresh-token.dat")));
        Assert.AreEqual(deviceId, await auth.EnsureDeviceRegisteredAsync("PC"));
        Assert.IsTrue(File.Exists(Path.Combine(dir, "device-registration-op.txt")));
        await auth.LogoutAsync();
        Assert.IsFalse(auth.IsSignedIn);
        Assert.IsNull(auth.CurrentUser);
        Assert.IsNull(auth.DeviceId);
        Assert.IsFalse(File.Exists(Path.Combine(dir, "refresh-token.dat")));
        Assert.IsFalse(File.Exists(Path.Combine(dir, "device.json")));
        Assert.IsFalse(File.Exists(Path.Combine(dir, "device-registration-op.txt")));

        using var req = new HttpRequestMessage(HttpMethod.Post, "api/auth/refresh");
        req.Headers.Add("X-CSRF", "1");
        using var res = await auth.SendWithAuthAsync(req, allowRefresh: false);
        Assert.AreEqual(HttpStatusCode.Unauthorized, res.StatusCode);
    }

    [TestMethod]
    public async Task Restart_RestoresRefreshCookie_AndRefreshesSession()
    {
        var handler = new StubHandler(req =>
        {
            if (req.RequestUri!.AbsolutePath.EndsWith("/api/auth/login"))
            {
                var res = JsonResponse(HttpStatusCode.OK, new
                {
                    accessToken = "jwt-first",
                    user = new { id = Guid.NewGuid(), email = "u@x.dev", fullName = "U" }
                });
                res.Headers.Add("Set-Cookie", "refreshToken=rt-123; Path=/api/auth; HttpOnly");
                return res;
            }
            if (req.RequestUri!.AbsolutePath.EndsWith("/api/auth/refresh"))
            {
                var sent = req.Headers.TryGetValues("Cookie", out var v) ? string.Join(";", v) : string.Empty;
                if (!sent.Contains("refreshToken=rt-123"))
                    return new HttpResponseMessage(HttpStatusCode.Unauthorized);
                var res = JsonResponse(HttpStatusCode.OK, new { accessToken = "jwt-second" });
                res.Headers.Add("Set-Cookie", "refreshToken=rt-456; Path=/api/auth; HttpOnly");
                return res;
            }
            var authHeader = req.Headers.Authorization?.Parameter;
            if (authHeader == "jwt-second")
                return JsonResponse(HttpStatusCode.OK, new { ok = true, via = "refreshed-jwt" });
            return new HttpResponseMessage(HttpStatusCode.Unauthorized);
        });
        var dir = TempDir();
        using (var auth = new DesktopAuthService("http://localhost:5000", dir, handler))
        {
            await auth.LoginAsync("u@x.dev", "password123");
            Assert.IsTrue(auth.IsSignedIn);
        }
        Assert.IsTrue(File.Exists(Path.Combine(dir, "refresh-token.dat")));

        using (var restarted = new DesktopAuthService("http://localhost:5000", dir, handler))
        {
            Assert.IsFalse(restarted.IsSignedIn);
            using var req = new HttpRequestMessage(HttpMethod.Get, "api/sync/cursor");
            using var res = await restarted.SendWithAuthAsync(req, allowRefresh: true);
            Assert.AreEqual(HttpStatusCode.OK, res.StatusCode);
            var doc = await ApiDocAsync(res);
            Assert.AreEqual("refreshed-jwt", doc!.RootElement.GetProperty("via").GetString());
            using var req2 = new HttpRequestMessage(HttpMethod.Get, "api/sync/cursor");
            using var res2 = await restarted.SendWithAuthAsync(req2, allowRefresh: true);
            Assert.AreEqual(HttpStatusCode.OK, res2.StatusCode);
        }

        var rotated = new NexSync.Desktop.Auth.SecureTokenStore(Path.Combine(dir, "refresh-token.dat")).Load();
        Assert.AreEqual("rt-456", rotated);
    }

    private static async Task<System.Text.Json.JsonDocument?> ApiDocAsync(HttpResponseMessage res)
    {
        var body = await res.Content.ReadAsStringAsync();
        if (string.IsNullOrEmpty(body)) return null;
        try { return System.Text.Json.JsonDocument.Parse(body); } catch { return null; }
    }

    [TestMethod]
    public async Task SendWithAuth_Forbidden_DoesNotTriggerReregistration()
    {
        var devicePosts = 0;
        var handler = new StubHandler(req =>
        {
            if (req.RequestUri!.AbsolutePath.EndsWith("/api/devices"))
            {
                devicePosts++;
                return new HttpResponseMessage(HttpStatusCode.Forbidden);
            }
            return new HttpResponseMessage(HttpStatusCode.OK);
        });
        using var auth = new DesktopAuthService("http://localhost:5000", TempDir(), handler);
        using var req = new HttpRequestMessage(HttpMethod.Get, "api/sync/pull?since=0&until=10&limit=10");
        using var res = await auth.SendWithAuthAsync(req, allowRefresh: true);
        Assert.AreEqual(HttpStatusCode.OK, res.StatusCode);
        Assert.AreEqual(0, devicePosts);
        Assert.IsNull(auth.DeviceId);
    }

    [TestMethod]
    public async Task RegistrationOperationId_PersistedAcrossInstances()
    {
        var opIds = new System.Collections.Generic.List<string?>();
        var handler = new StubHandler(req =>
        {
            opIds.Add(req.Headers.TryGetValues("X-Operation-Id", out var o) ? string.Join(",", o) : null);
            throw new HttpRequestException("Simulated network failure before server commit.");
        });
        var dir = TempDir();
        using (var auth = new DesktopAuthService("http://localhost:5000", dir, handler))
        {
            await Assert.ThrowsExactlyAsync<HttpRequestException>(() => auth.EnsureDeviceRegisteredAsync("PC"));
        }
        using (var auth2 = new DesktopAuthService("http://localhost:5000", dir, handler))
        {
            await Assert.ThrowsExactlyAsync<HttpRequestException>(() => auth2.EnsureDeviceRegisteredAsync("PC"));
        }
        Assert.AreEqual(2, opIds.Count);
        Assert.AreEqual(opIds[0], opIds[1]);
        Assert.IsNotNull(opIds[0]);
    }

    [TestMethod]
    public async Task Login_UnreachableServer_ThrowsHttpRequestException()
    {
        var handler = new StubHandler(_ => throw new HttpRequestException("No connection."));
        using var auth = new DesktopAuthService("http://localhost:5000", TempDir(), handler);
        await Assert.ThrowsExactlyAsync<HttpRequestException>(() => auth.LoginAsync("u@x.dev", "x"));
        Assert.IsFalse(auth.IsSignedIn);
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
