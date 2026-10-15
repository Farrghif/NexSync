using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace NexSync.Android.Services;

public sealed class ApiClient : IDisposable
{
    private readonly HttpClient _http;
    private readonly CookieContainer _cookies = new();
    private readonly SemaphoreSlim _refreshLock = new(1, 1);
    private string? _accessToken;
    private bool _disposed;

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public ApiClient()
    {
        var handler = new HttpClientHandler
        {
            CookieContainer = _cookies,
            UseCookies = true
        };
        _http = new HttpClient(handler)
        {
            Timeout = TimeSpan.FromSeconds(30)
        };
        RestoreSession();
    }

    private string BaseUrl => ApiConfig.BaseUrl.TrimEnd('/');

    private void RestoreSession()
    {
        _accessToken = SecureStore.GetAccessToken();
        var saved = Preferences.Default.Get<string?>("nexsync_refresh_cookie", null);
        if (!string.IsNullOrEmpty(saved))
        {
            try { _cookies.Add(new Uri(BaseUrl + "/"), new Cookie("refreshToken", saved) { Path = "/api/auth" }); } catch { }
        }
    }

    private void PersistRefreshCookie()
    {
        try
        {
            var c = _cookies.GetCookies(new Uri(BaseUrl + "/"))["refreshToken"];
            if (c is not null && !string.IsNullOrEmpty(c.Value))
                Preferences.Default.Set("nexsync_refresh_cookie", c.Value);
        }
        catch { }
    }

    private void ClearRefreshCookie()
    {
        try
        {
            foreach (Cookie c in _cookies.GetCookies(new Uri(BaseUrl + "/")))
                c.Expired = true;
        }
        catch { }
        Preferences.Default.Remove("nexsync_refresh_cookie");
    }

    public string? AccessToken => _accessToken;

    public void SetAccessToken(string? token)
    {
        _accessToken = token;
        SecureStore.SetAccessToken(token);
    }

    private string? CurrentDeviceId => SecureStore.CurrentDeviceId;

    private bool IsDeviceRegistration(HttpRequestMessage req)
    {
        var path = req.RequestUri?.IsAbsoluteUri == true
            ? req.RequestUri.AbsolutePath
            : req.RequestUri?.OriginalString ?? "";
        return path.EndsWith("/api/devices", StringComparison.OrdinalIgnoreCase);
    }

    private Uri ResolveUri(string path)
    {
        if (path.StartsWith("http", StringComparison.OrdinalIgnoreCase))
            return new Uri(path);
        return new Uri($"{BaseUrl}/{path.TrimStart('/')}");
    }

    private async Task<HttpResponseMessage> SendWithAuthAsync(
        HttpRequestMessage request, bool allowRefresh, CancellationToken ct)
    {
        if (!string.IsNullOrEmpty(_accessToken) && request.Headers.Authorization is null)
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _accessToken);

        if (!string.IsNullOrEmpty(CurrentDeviceId)
            && !request.Headers.Contains("X-Device-Id")
            && !IsDeviceRegistration(request))
            request.Headers.Add("X-Device-Id", CurrentDeviceId!);

        var res = await _http.SendAsync(request, ct);
        PersistRefreshCookie();

        if (res.StatusCode != HttpStatusCode.Unauthorized || !allowRefresh)
            return res;

        res.Dispose();
        var refreshed = await TryRefreshAsync(ct);
        if (!refreshed)
        {
            var denied = new HttpResponseMessage(HttpStatusCode.Unauthorized);
            denied.RequestMessage = request;
            return denied;
        }

        var retry = await CloneRequestAsync(request, ct);
        if (!string.IsNullOrEmpty(_accessToken))
            retry.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _accessToken);
        if (!string.IsNullOrEmpty(CurrentDeviceId)
            && !retry.Headers.Contains("X-Device-Id")
            && !IsDeviceRegistration(retry))
            retry.Headers.Add("X-Device-Id", CurrentDeviceId!);

        var res2 = await _http.SendAsync(retry, ct);
        PersistRefreshCookie();
        return res2;
    }

    private async Task<bool> TryRefreshAsync(CancellationToken ct)
    {
        await _refreshLock.WaitAsync(ct);
        try
        {
            var req = new HttpRequestMessage(HttpMethod.Post, ResolveUri("api/auth/refresh"));
            req.Headers.Add("X-CSRF", "1");
            var res = await _http.SendAsync(req, ct);
            PersistRefreshCookie();
            if (!res.IsSuccessStatusCode) return false;
            var body = await res.Content.ReadAsStringAsync(ct);
            using var doc = JsonDocument.Parse(body);
            var token = doc.RootElement.GetProperty("accessToken").GetString();
            if (string.IsNullOrEmpty(token)) return false;
            SetAccessToken(token);
            return true;
        }
        catch { return false; }
        finally { _refreshLock.Release(); }
    }

    private static async Task<HttpRequestMessage> CloneRequestAsync(HttpRequestMessage original, CancellationToken ct)
    {
        var clone = new HttpRequestMessage(original.Method, original.RequestUri);
        foreach (var h in original.Headers)
            clone.Headers.TryAddWithoutValidation(h.Key, h.Value);
        if (original.Content is not null)
        {
            var bytes = await original.Content.ReadAsByteArrayAsync(ct);
            var content = new ByteArrayContent(bytes);
            foreach (var h in original.Content.Headers)
                content.Headers.TryAddWithoutValidation(h.Key, h.Value);
            clone.Content = content;
        }
        return clone;
    }

    public async Task<(HttpResponseMessage Response, JsonDocument? Doc)> SendAsync(
        HttpMethod method, string path, object? body = null,
        Dictionary<string, string>? headers = null, bool allowRefresh = true, CancellationToken ct = default)
    {
        var req = new HttpRequestMessage(method, ResolveUri(path));
        if (headers is not null)
            foreach (var kv in headers)
                req.Headers.TryAddWithoutValidation(kv.Key, kv.Value);
        if (body is not null)
        {
            var json = JsonSerializer.Serialize(body, JsonOpts);
            req.Content = new StringContent(json, Encoding.UTF8, "application/json");
        }
        var res = await SendWithAuthAsync(req, allowRefresh, ct);
        JsonDocument? doc = null;
        var raw = await res.Content.ReadAsStringAsync(ct);
        if (!string.IsNullOrWhiteSpace(raw))
            try { doc = JsonDocument.Parse(raw); } catch { }
        return (res, doc);
    }

    public async Task<(HttpResponseMessage Response, JsonDocument? Doc)> UploadAsync(
        string path, Stream fileStream, string fileName, string contentType,
        Dictionary<string, string>? headers = null, IProgress<int>? progress = null, CancellationToken ct = default)
    {
        using var form = new MultipartFormDataContent();
        var sc = new StreamContent(fileStream);
        sc.Headers.ContentType = new MediaTypeHeaderValue(contentType);
        form.Add(sc, "file", fileName);

        var req = new HttpRequestMessage(HttpMethod.Post, ResolveUri(path));
        if (headers is not null)
            foreach (var kv in headers)
                req.Headers.TryAddWithoutValidation(kv.Key, kv.Value);
        req.Content = form;

        var res = await SendWithAuthAsync(req, true, ct);
        PersistRefreshCookie();
        JsonDocument? doc = null;
        var raw = await res.Content.ReadAsStringAsync(ct);
        if (!string.IsNullOrWhiteSpace(raw))
            try { doc = JsonDocument.Parse(raw); } catch { }
        return (res, doc);
    }

    public void ClearAuth()
    {
        _accessToken = null;
        SecureStore.SetAccessToken(null);
        ClearRefreshCookie();
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _http.Dispose();
        _refreshLock.Dispose();
    }
}
