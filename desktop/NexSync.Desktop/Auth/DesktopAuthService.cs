using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace NexSync.Desktop.Auth;

public sealed record UserInfo(Guid Id, string Email, string FullName);

public sealed class DesktopAuthService : IDisposable
{
    private readonly HttpClient _http;
    private readonly SecureTokenStore _tokenStore;
    private readonly string _configPath;
    private readonly SemaphoreSlim _refreshLock = new(1, 1);
    private string? _accessToken;

    public string? DeviceId { get; private set; }
    public UserInfo? CurrentUser { get; private set; }
    public bool IsSignedIn => CurrentUser is not null && _accessToken is not null;

    public DesktopAuthService(string baseUrl, string dataDirectory)
        : this(baseUrl, dataDirectory, new HttpClientHandler
        {
            CookieContainer = new CookieContainer(),
            UseCookies = true
        })
    {
    }

    public DesktopAuthService(string baseUrl, string dataDirectory, HttpMessageHandler handler)
    {
        Directory.CreateDirectory(dataDirectory);
        _tokenStore = new SecureTokenStore(Path.Combine(dataDirectory, "refresh-token.dat"));
        _configPath = Path.Combine(dataDirectory, "device.json");
        _http = new HttpClient(handler) { BaseAddress = new Uri(baseUrl.TrimEnd('/') + "/") };
        DeviceId = LoadDeviceId();
    }

    public async Task<UserInfo> LoginAsync(string email, string password, CancellationToken ct = default)
    {
        using var res = await _http.PostAsJsonAsync("api/auth/login", new { email, password }, ct);
        if (res.StatusCode == HttpStatusCode.Unauthorized)
            throw new UnauthorizedException("Invalid email or password.");
        res.EnsureSuccessStatusCode();
        return await AcceptAuthResponseAsync(res, ct);
    }

    public async Task<UserInfo> RegisterAsync(string email, string password, string fullName, CancellationToken ct = default)
    {
        using var res = await _http.PostAsJsonAsync("api/auth/register", new { email, password, fullName }, ct);
        res.EnsureSuccessStatusCode();
        return await AcceptAuthResponseAsync(res, ct);
    }

    public async Task LogoutAsync(CancellationToken ct = default)
    {
        try
        {
            using var req = new HttpRequestMessage(HttpMethod.Post, "api/auth/logout");
            req.Headers.Add("X-CSRF", "1");
            using var res = await SendWithAuthAsync(req, allowRefresh: false, ct);
            res.EnsureSuccessStatusCode();
        }
        finally
        {
            _accessToken = null;
            CurrentUser = null;
            _tokenStore.Clear();
        }
    }

    public async Task<Guid> EnsureDeviceRegisteredAsync(string deviceName, CancellationToken ct = default)
    {
        if (DeviceId is not null && Guid.TryParse(DeviceId, out var existing))
            return existing;
        var operationId = LoadOrCreateRegistrationOperationId();
        using var req = new HttpRequestMessage(HttpMethod.Post, "api/devices");
        req.Content = JsonContent.Create(new { name = deviceName, platform = "Windows" });
        req.Headers.Add("X-Operation-Id", operationId.ToString());
        using var res = await SendWithAuthAsync(req, allowRefresh: true, ct);
        res.EnsureSuccessStatusCode();
        using var doc = JsonDocument.Parse(await res.Content.ReadAsStringAsync(ct));
        var id = doc.RootElement.GetProperty("deviceId").GetGuid();
        PersistDeviceId(id.ToString());
        return id;
    }

    public async Task<HttpResponseMessage> SendWithAuthAsync(HttpRequestMessage request, bool allowRefresh, CancellationToken ct = default)
    {
        if (_accessToken is not null)
            request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", _accessToken);
        if (DeviceId is not null && !request.Headers.Contains("X-Device-Id") && !request.RequestUri!.AbsolutePath.EndsWith("/api/devices"))
            request.Headers.Add("X-Device-Id", DeviceId);
        var res = await _http.SendAsync(request, ct);
        if (res.StatusCode != HttpStatusCode.Unauthorized || !allowRefresh)
            return res;
        res.Dispose();
        var refreshed = await TryRefreshAsync(ct);
        if (!refreshed)
            return res;
        var retry = await CloneRequestAsync(request, ct);
        retry.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", _accessToken);
        return await _http.SendAsync(retry, ct);
    }

    private async Task<bool> TryRefreshAsync(CancellationToken ct)
    {
        await _refreshLock.WaitAsync(ct);
        try
        {
            using var req = new HttpRequestMessage(HttpMethod.Post, "api/auth/refresh");
            req.Headers.Add("X-CSRF", "1");
            using var res = await _http.SendAsync(req, ct);
            if (!res.IsSuccessStatusCode)
                return false;
            using var doc = JsonDocument.Parse(await res.Content.ReadAsStringAsync(ct));
            _accessToken = doc.RootElement.GetProperty("accessToken").GetString();
            return _accessToken is not null;
        }
        finally
        {
            _refreshLock.Release();
        }
    }

    private async Task<UserInfo> AcceptAuthResponseAsync(HttpResponseMessage res, CancellationToken ct)
    {
        using var doc = JsonDocument.Parse(await res.Content.ReadAsStringAsync(ct));
        var userEl = doc.RootElement.GetProperty("user");
        var user = new UserInfo(
            userEl.GetProperty("id").GetGuid(),
            userEl.GetProperty("email").GetString()!,
            userEl.GetProperty("fullName").GetString()!);
        _accessToken = doc.RootElement.GetProperty("accessToken").GetString();
        CurrentUser = user;
        return user;
    }

    private string? LoadDeviceId()
    {
        if (!File.Exists(_configPath))
            return null;
        try
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(_configPath));
            return doc.RootElement.GetProperty("deviceId").GetString();
        }
        catch (Exception)
        {
            return null;
        }
    }

    private void PersistDeviceId(string deviceId)
    {
        DeviceId = deviceId;
        var tempPath = _configPath + ".tmp-" + Guid.NewGuid().ToString("N");
        try
        {
            File.WriteAllText(tempPath, JsonSerializer.Serialize(new { deviceId }));
            File.Move(tempPath, _configPath, overwrite: true);
        }
        finally
        {
            if (File.Exists(tempPath))
            {
                try { File.Delete(tempPath); } catch (IOException) { }
            }
        }
    }

    private Guid LoadOrCreateRegistrationOperationId()
    {
        var opPath = Path.Combine(Path.GetDirectoryName(_configPath)!, "device-registration-op.txt");
        if (File.Exists(opPath) && Guid.TryParse(File.ReadAllText(opPath).Trim(), out var existing))
            return existing;
        var created = Guid.NewGuid();
        File.WriteAllText(opPath, created.ToString());
        return created;
    }

    private static async Task<HttpRequestMessage> CloneRequestAsync(HttpRequestMessage original, CancellationToken ct)
    {
        var clone = new HttpRequestMessage(original.Method, original.RequestUri);
        foreach (var header in original.Headers)
            clone.Headers.TryAddWithoutValidation(header.Key, header.Value);
        if (original.Content is not null)
        {
            var bytes = await original.Content.ReadAsByteArrayAsync(ct);
            var content = new ByteArrayContent(bytes);
            foreach (var header in original.Content.Headers)
                content.Headers.TryAddWithoutValidation(header.Key, header.Value);
            clone.Content = content;
        }
        return clone;
    }

    public void Dispose()
    {
        _http.Dispose();
        _refreshLock.Dispose();
    }
}
