using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

namespace NexSync.Tests;

public sealed class ApiClient(HttpClient http)
{
    private readonly HttpClient _http = http;
    public string? AccessToken { get; private set; }
    public string? RefreshCookie { get; set; }

    private static readonly JsonSerializerOptions Json = new() { PropertyNameCaseInsensitive = true };

    private void ApplyAuth(HttpRequestMessage req)
    {
        if (AccessToken is not null)
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", AccessToken);
        if (RefreshCookie is not null)
            req.Headers.Add("Cookie", $"refreshToken={RefreshCookie}");
    }

    private static string? ExtractRefreshCookie(HttpResponseMessage res)
    {
        if (!res.Headers.TryGetValues("Set-Cookie", out var values)) return null;
        foreach (var v in values)
        {
            var semi = v.IndexOf(';');
            var pair = semi >= 0 ? v[..semi] : v;
            if (pair.StartsWith("refreshToken=", StringComparison.Ordinal))
                return pair["refreshToken=".Length..];
        }
        return null;
    }

    private async Task<(HttpResponseMessage Res, JsonDocument? Doc)> SendAsync(HttpRequestMessage req)
    {
        ApplyAuth(req);
        var res = await _http.SendAsync(req);
        JsonDocument? doc = null;
        var body = await res.Content.ReadAsStringAsync();
        if (body.Length > 0)
        {
            try { doc = JsonDocument.Parse(body); } catch { }
        }
        var cookie = ExtractRefreshCookie(res);
        if (cookie is not null) RefreshCookie = cookie;
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
        req.Headers.Add("X-CSRF", "1");
        var (res, doc) = await SendAsync(req);
        if (res.IsSuccessStatusCode && doc is not null)
            AccessToken = doc.RootElement.GetProperty("accessToken").GetString();
        return (res, doc);
    }

    public async Task<HttpResponseMessage> LogoutAsync()
    {
        var req = new HttpRequestMessage(HttpMethod.Post, "/api/auth/logout");
        req.Headers.Add("X-CSRF", "1");
        var (res, _) = await SendAsync(req);
        return res;
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



