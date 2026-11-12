using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace NexSync.Desktop.Tests;

// Runs against the real backend on localhost:5189 (docker compose + dotnet run).
// Skipped automatically when the backend is not listening.
[TestClass]
public sealed class EndToEndTests
{
    private const string BaseUrl = "http://localhost:5189";
    private static readonly TimeSpan PollTimeout = TimeSpan.FromSeconds(20);

    private string _dir = null!;
    private string _root = null!;

    [TestInitialize]
    public void Setup()
    {
        if (!BackendListening())
            Assert.Inconclusive("Backend not listening on localhost:5189 - start docker compose + dotnet run first.");
        _dir = Path.Combine(Path.GetTempPath(), "nexsync-e2e", Guid.NewGuid().ToString("N"));
        _root = Path.Combine(_dir, "files");
        Directory.CreateDirectory(_root);
    }

    [TestCleanup]
    public void Teardown()
    {
        if (Directory.Exists(_dir))
            Directory.Delete(_dir, recursive: true);
    }

    private static bool BackendListening()
    {
        try
        {
            using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(2) };
            // Any HTTP response (even 405) proves the server is up.
            client.SendAsync(new HttpRequestMessage(HttpMethod.Get, BaseUrl + "/api/auth/login")).GetAwaiter().GetResult();
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static async Task<bool> WaitUntilAsync(Func<Task<bool>> condition)
    {
        var deadline = DateTime.UtcNow + PollTimeout;
        while (DateTime.UtcNow < deadline)
        {
            if (await condition()) return true;
            await Task.Delay(500);
        }
        return false;
    }

    private static async Task<JsonDocument> ReadJsonAsync(HttpResponseMessage res)
        => JsonDocument.Parse(await res.Content.ReadAsStringAsync());

    [TestMethod]
    public async Task Login_PushAndPull_ConvergeWithBackend()
    {
        var email = $"e2e-{Guid.NewGuid():N}@nexsync.test";
        const string password = "E2e-Passw0rd!";
        using var http = new HttpClient { BaseAddress = new Uri(BaseUrl), Timeout = TimeSpan.FromSeconds(15) };

        // --- 1. Register a fresh user (the "web" side gets its own token) ---
        using var regRes = await http.PostAsync("/api/auth/register", new StringContent(
            JsonSerializer.Serialize(new { email, password, fullName = "E2E Test" }),
            Encoding.UTF8, "application/json"));
        Assert.AreEqual(HttpStatusCode.OK, regRes.StatusCode, "register must succeed: " + await regRes.Content.ReadAsStringAsync());
        using var regJson = await ReadJsonAsync(regRes);
        var webToken = regJson.RootElement.GetProperty("accessToken").GetString();
        Assert.IsFalse(string.IsNullOrEmpty(webToken));

        // --- 2. Desktop login through the real auth pipeline ---
        using var services = new DesktopServices(BaseUrl, Path.Combine(_dir, "data"));
        var user = await services.Auth.LoginAsync(email, password);
        Assert.AreEqual(email, user.Email, "desktop login must return the registered user");
        var deviceId = await services.Auth.EnsureDeviceRegisteredAsync(Environment.MachineName);
        Assert.AreNotEqual(Guid.Empty, deviceId, "device registration must yield a device id");

        var bearer = new AuthenticationHeaderValue("Bearer", webToken);

        // --- 3. Start the engine (SetLocalRoot starts the background sync loop) ---
        services.State.SetLocalRoot(_root);

        // --- 4. PULL: upload a file "from the web", expect it on the desktop disk ---
        var webBytes = Encoding.UTF8.GetBytes("hello from web " + Guid.NewGuid());
        using var form = new MultipartFormDataContent();
        using var fileContent = new ByteArrayContent(webBytes);
        fileContent.Headers.ContentType = new MediaTypeHeaderValue("text/plain");
        form.Add(fileContent, "file", "e2e-pull.txt");
        using var uploadRes = await SendAsync(http, HttpMethod.Post, "/api/files/upload", form, bearer, deviceId);
        Assert.AreEqual(HttpStatusCode.Created, uploadRes.StatusCode,
            "web upload must succeed: " + await uploadRes.Content.ReadAsStringAsync());

        var pulledPath = Path.Combine(_root, "e2e-pull.txt");
        Assert.IsTrue(await WaitUntilAsync(() => Task.FromResult(File.Exists(pulledPath))),
            "file uploaded by the web client must be pulled down to the desktop folder");
        CollectionAssert.AreEqual(webBytes, await File.ReadAllBytesAsync(pulledPath),
            "pulled content must match what the web client uploaded");

        // --- 5. PUSH: create a file on the desktop disk, expect it on the backend ---
        var localPath = Path.Combine(_root, "e2e-push.txt");
        var localBytes = Encoding.UTF8.GetBytes("hello from desktop " + Guid.NewGuid());
        await File.WriteAllBytesAsync(localPath, localBytes);

        var pushed = await WaitUntilAsync(async () =>
        {
            using var res = await SendAsync(http, HttpMethod.Get, "/api/folders?page=1&pageSize=100", null, bearer, deviceId);
            if (res.StatusCode != HttpStatusCode.OK) return false;
            using var json = await ReadJsonAsync(res);
            var files = json.RootElement.GetProperty("files");
            return files.EnumerateArray().Any(f => f.GetProperty("name").GetString() == "e2e-push.txt");
        });
        Assert.IsTrue(pushed, "file created on the desktop disk must be pushed to the backend");

        // Verify the pushed bytes round-trip through the backend download endpoint.
        Guid fileId;
        using (var res = await SendAsync(http, HttpMethod.Get, "/api/folders?page=1&pageSize=100", null, bearer, deviceId))
        using (var json = await ReadJsonAsync(res))
        {
            fileId = json.RootElement.GetProperty("files").EnumerateArray()
                .First(f => f.GetProperty("name").GetString() == "e2e-push.txt")
                .GetProperty("id").GetGuid();
        }
        using var dl = await SendAsync(http, HttpMethod.Get, $"/api/files/{fileId}/download", null, bearer, deviceId);
        Assert.AreEqual(HttpStatusCode.OK, dl.StatusCode);
        CollectionAssert.AreEqual(localBytes, await dl.Content.ReadAsByteArrayAsync(),
            "content downloaded from the backend must match the desktop file");
    }

    private static async Task<HttpResponseMessage> SendAsync(
        HttpClient http, HttpMethod method, string path, HttpContent? content, AuthenticationHeaderValue bearer, Guid deviceId)
    {
        using var req = new HttpRequestMessage(method, path);
        req.Headers.Authorization = bearer;
        req.Headers.Add("X-Device-Id", deviceId.ToString());
        req.Content = content;
        // Multipart content cannot be reused across retries; send directly.
        return await http.SendAsync(req);
    }
}
