using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

namespace NexSync.Tests;

public sealed class ApiClient(HttpClient http)
{
    private readonly HttpClient _http = http;
    public string? AccessToken { get; private set; }
    public string? RefreshCookie { get; set; }
    public string? SessionCookie { get; set; }
    public string? CsrfToken { get; set; }
    public Guid? DeviceId { get; set; }

    private void ApplyDevice(HttpRequestMessage req)
    {
        if (DeviceId.HasValue)
            req.Headers.Add("X-Device-Id", DeviceId.Value.ToString());
    }

    private static readonly JsonSerializerOptions Json = new() { PropertyNameCaseInsensitive = true };

    private void ApplyAuth(HttpRequestMessage req)
    {
        if (AccessToken is not null)
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", AccessToken);
        var cookies = new List<string>();
        if (RefreshCookie is not null) cookies.Add($"refreshToken={RefreshCookie}");
        if (SessionCookie is not null) cookies.Add($".AspNetCore.Session={SessionCookie}");
        if (cookies.Count > 0)
            req.Headers.Add("Cookie", string.Join("; ", cookies));
    }

    private static string? ExtractCookie(HttpResponseMessage res, string name)
    {
        if (!res.Headers.TryGetValues("Set-Cookie", out var values)) return null;
        foreach (var v in values)
        {
            var semi = v.IndexOf(';');
            var pair = semi >= 0 ? v[..semi] : v;
            if (pair.StartsWith(name + "=", StringComparison.Ordinal))
                return pair[(name.Length + 1)..];
        }
        return null;
    }

    private void CaptureState(HttpResponseMessage res, string body)
    {
        var cookie = ExtractCookie(res, "refreshToken");
        if (cookie is not null) RefreshCookie = cookie;
        var session = ExtractCookie(res, ".AspNetCore.Session");
        if (session is not null) SessionCookie = session;
        if (body.Length == 0) return;
        try
        {
            using var doc = JsonDocument.Parse(body);
            if (doc.RootElement.ValueKind == JsonValueKind.Object &&
                doc.RootElement.TryGetProperty("csrfToken", out var token))
                CsrfToken = token.GetString();
        }
        catch (System.Text.Json.JsonException) { }
    }

    private async Task<(HttpResponseMessage Res, JsonDocument? Doc)> SendAsync(HttpRequestMessage req)
    {
        ApplyAuth(req);
        ApplyDevice(req);
        var res = await _http.SendAsync(req);
        JsonDocument? doc = null;
        var body = await res.Content.ReadAsStringAsync();
        if (body.Length > 0)
        {
            try { doc = JsonDocument.Parse(body); } catch { }
        }
        CaptureState(res, body);
        return (res, doc);
    }

    public async Task<(HttpResponseMessage, JsonDocument?)> RegisterAsync(string email, string password, string name)
    {
        var req = new HttpRequestMessage(HttpMethod.Post, "/api/auth/register")
        {
            Content = JsonContent.Create(new { email, password, fullName = name })
        };
        var (res, doc) = await SendAsync(req);
        if (res.IsSuccessStatusCode && doc is not null)
            AccessToken = doc.RootElement.GetProperty("accessToken").GetString();
        return (res, doc);
    }

    public async Task<(HttpResponseMessage, JsonDocument?)> LoginAsync(string email, string password)
    {
        var req = new HttpRequestMessage(HttpMethod.Post, "/api/auth/login")
        {
            Content = JsonContent.Create(new { email, password })
        };
        var (res, doc) = await SendAsync(req);
        if (res.IsSuccessStatusCode && doc is not null)
            AccessToken = doc.RootElement.GetProperty("accessToken").GetString();
        return (res, doc);
    }

    public async Task<(HttpResponseMessage, JsonDocument?)> RefreshAsync()
    {
        var req = new HttpRequestMessage(HttpMethod.Post, "/api/auth/refresh");
        if (CsrfToken is not null) req.Headers.Add("X-CSRF-Token", CsrfToken);
        var (res, doc) = await SendAsync(req);
        if (res.IsSuccessStatusCode && doc is not null)
            AccessToken = doc.RootElement.GetProperty("accessToken").GetString();
        return (res, doc);
    }

    public async Task<HttpResponseMessage> LogoutAsync()
    {
        var req = new HttpRequestMessage(HttpMethod.Post, "/api/auth/logout");
        if (CsrfToken is not null) req.Headers.Add("X-CSRF-Token", CsrfToken);
        var (res, _) = await SendAsync(req);
        return res;
    }

    public async Task<HttpResponseMessage> SendRawAsync(HttpRequestMessage req)
    {
        ApplyAuth(req);
        ApplyDevice(req);
        var res = await _http.SendAsync(req);
        CaptureState(res, await res.Content.ReadAsStringAsync());
        return res;
    }

    public static async Task<JsonDocument?> ParseAsync(HttpResponseMessage res)
    {
        var body = await res.Content.ReadAsStringAsync();
        if (body.Length == 0) return null;
        try { return JsonDocument.Parse(body); } catch { return null; }
    }

    public async Task<(HttpResponseMessage, JsonDocument?)> GetAsync(string url)
        => await SendAsync(new HttpRequestMessage(HttpMethod.Get, url));

    public async Task<(HttpResponseMessage, JsonDocument?)> PostJsonAsync(string url, object body)
    {
        var req = new HttpRequestMessage(HttpMethod.Post, url) { Content = JsonContent.Create(body) };
        return await SendAsync(req);
    }

    public async Task<(HttpResponseMessage, JsonDocument?)> PutJsonAsync(string url, object body)
    {
        var req = new HttpRequestMessage(HttpMethod.Put, url) { Content = JsonContent.Create(body) };
        return await SendAsync(req);
    }

    public async Task<HttpResponseMessage> DeleteAsync(string url)
    {
        var (res, _) = await SendAsync(new HttpRequestMessage(HttpMethod.Delete, url));
        return res;
    }

    public async Task<(HttpResponseMessage, JsonDocument?)> UploadAsync(byte[] content, string fileName, string contentType, Guid? folderId = null)
    {
        using var form = new MultipartFormDataContent();
        var byteContent = new ByteArrayContent(content);
        byteContent.Headers.ContentType = new MediaTypeHeaderValue(contentType);
        form.Add(byteContent, "file", fileName);
        var url = "/api/files/upload" + (folderId.HasValue ? $"?folderId={folderId}" : "");
        var req = new HttpRequestMessage(HttpMethod.Post, url) { Content = form };
        return await SendAsync(req);
    }

    public async Task<HttpResponseMessage> DownloadRawAsync(Guid fileId)
    {
        var req = new HttpRequestMessage(HttpMethod.Get, $"/api/files/{fileId}/download");
        ApplyAuth(req);
        return await _http.SendAsync(req);
    }
}



