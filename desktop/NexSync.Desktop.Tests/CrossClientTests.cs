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

// Cross-client convergence: the SAME account must be usable from desktop, web and
// Android, and all three must see the same file list. Runs against the real backend
// on localhost:5189 (skipped when it is not listening).
//
// Execution order (evidence run):
//   1. DesktopLogin_PushesFileToSharedAccount
//   2. (web browser uploads cross-web.txt via the dashboard)
//   3. (android emulator logs into the same account and lists the files)
//   4. DesktopAfterWebAndAndroid_SeesIdenticalData
[TestClass]
public sealed class CrossClientTests
{
    public const string AccountEmail = "crossclient@nexsync.test";
    public const string AccountPassword = "CrossClient-2026!";
    public const string DesktopFileName = "cross-desktop.txt";
    public const string WebFileName = "cross-web.txt";
    private const string BaseUrl = "http://localhost:5189";
    private static readonly TimeSpan PollTimeout = TimeSpan.FromSeconds(30);

    private string _dir = null!;

    [TestInitialize]
    public void Setup()
    {
        if (!BackendListening())
            Assert.Inconclusive("Backend not listening on localhost:5189.");
        _dir = Path.Combine(Path.GetTempPath(), "nexsync-cross", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dir);
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

    private static async Task<JsonDocument> ListRootAsync(Desktop.Auth.DesktopAuthService auth)
    {
        using var req = new HttpRequestMessage(HttpMethod.Get, "/api/folders?page=1&pageSize=100");
        using var res = await auth.SendWithAuthAsync(req, allowRefresh: true);
        res.EnsureSuccessStatusCode();
        return JsonDocument.Parse(await res.Content.ReadAsStringAsync());
    }

    [TestMethod]
    public async Task DesktopLogin_PushesFileToSharedAccount()
    {
        using var services = new DesktopServices(BaseUrl, Path.Combine(_dir, "data"));

        // 1. Desktop login with the shared account (exact code path LoginView uses).
        var user = await services.Auth.LoginAsync(AccountEmail, AccountPassword);
        Assert.AreEqual(AccountEmail, user.Email, "desktop must log into the shared account");
        var deviceId = await services.Auth.EnsureDeviceRegisteredAsync(Environment.MachineName);
        Assert.AreNotEqual(Guid.Empty, deviceId, "desktop device must be registered");

        // 2. Create a file BEFORE the engine starts - the same situation as a file
        //    added while the app was closed. The startup reconcile must push it.
        var root = Path.Combine(_dir, "files");
        Directory.CreateDirectory(root);
        var payload = Encoding.UTF8.GetBytes("data written by desktop " + Guid.NewGuid());
        await File.WriteAllBytesAsync(Path.Combine(root, DesktopFileName), payload);
        services.State.SetLocalRoot(root);

        var pushed = await WaitUntilAsync(async () =>
        {
            using var json = await ListRootAsync(services.Auth);
            return json.RootElement.GetProperty("files").EnumerateArray()
                .Any(f => f.GetProperty("name").GetString() == DesktopFileName);
        });
        Assert.IsTrue(pushed, $"desktop-created file must appear in the shared account on the backend");

        // 3. The bytes stored server-side must match what the desktop wrote.
        Guid fileId;
        using (var json = await ListRootAsync(services.Auth))
        {
            fileId = json.RootElement.GetProperty("files").EnumerateArray()
                .First(f => f.GetProperty("name").GetString() == DesktopFileName)
                .GetProperty("id").GetGuid();
        }
        using var dl = new HttpRequestMessage(HttpMethod.Get, $"/api/files/{fileId}/download");
        using var dlRes = await services.Auth.SendWithAuthAsync(dl, allowRefresh: true);
        Assert.AreEqual(HttpStatusCode.OK, dlRes.StatusCode);
        CollectionAssert.AreEqual(payload, await dlRes.Content.ReadAsByteArrayAsync(),
            "backend must store exactly the bytes the desktop pushed");
    }

    [TestMethod]
    public async Task DesktopAfterWebAndAndroid_SeesIdenticalData()
    {
        // Fresh local root: everything on disk must come from the shared account.
        using var services = new DesktopServices(BaseUrl, Path.Combine(_dir, "data"));
        var user = await services.Auth.LoginAsync(AccountEmail, AccountPassword);
        Assert.AreEqual(AccountEmail, user.Email);
        await services.Auth.EnsureDeviceRegisteredAsync(Environment.MachineName);

        var root = Path.Combine(_dir, "files");
        Directory.CreateDirectory(root);
        services.State.SetLocalRoot(root);
        await services.RunSyncCycleAsync();

        var sawDesktop = await WaitUntilAsync(() => Task.FromResult(
            File.Exists(Path.Combine(root, DesktopFileName))));
        Assert.IsTrue(sawDesktop, $"desktop must pull {DesktopFileName} (created by desktop earlier)");

        var sawWeb = await WaitUntilAsync(() => Task.FromResult(
            File.Exists(Path.Combine(root, WebFileName))));
        Assert.IsTrue(sawWeb, $"desktop must pull {WebFileName} (uploaded by the web client)");

        // Server-side list and local disk must agree.
        using var json = await ListRootAsync(services.Auth);
        var serverFiles = json.RootElement.GetProperty("files").EnumerateArray()
            .Select(f => f.GetProperty("name").GetString()).OrderBy(n => n).ToArray();
        var localFiles = Directory.GetFiles(root).Select(Path.GetFileName).OrderBy(n => n).ToArray();
        CollectionAssert.AreEqual(serverFiles, localFiles,
            "backend file list and desktop folder must contain exactly the same files");
    }
}
