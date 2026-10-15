using System.Net;
using System.Text.Json;
using NexSync.Android.Models;

namespace NexSync.Android.Services;

public sealed class FileService(ApiClient api)
{
    private static string NewOpId() => Guid.NewGuid().ToString();

    private static Dictionary<string, string> OpHeaders() => new() { ["X-Operation-Id"] = NewOpId() };

    private static readonly JsonSerializerOptions CaseInsensitive = new() { PropertyNameCaseInsensitive = true };

    public async Task<FolderContents> GetContentsAsync(string? parentId, CancellationToken ct = default)
    {
        var qs = $"api/folders?page=1&pageSize=50{(parentId is null ? "" : $"&parentId={Uri.EscapeDataString(parentId)}")}";
        var (res, doc) = await api.SendAsync(HttpMethod.Get, qs, ct: ct);
        if (!res.IsSuccessStatusCode) Throw(res, doc);
        var root = doc!.RootElement;
        var folders = new List<FolderDto>();
        foreach (var f in root.GetProperty("folders").EnumerateArray())
            folders.Add(ParseFolder(f));
        var files = new List<FileDto>();
        foreach (var f in root.GetProperty("files").EnumerateArray())
            files.Add(ParseFile(f));
        return new FolderContents(
            folders, files,
            root.TryGetProperty("page", out var p) ? p.GetInt32() : 1,
            root.TryGetProperty("pageSize", out var ps) ? ps.GetInt32() : 50,
            root.TryGetProperty("totalCount", out var tc) ? tc.GetInt32() : folders.Count + files.Count,
            root.TryGetProperty("totalPages", out var tp) ? tp.GetInt32() : 1);
    }

    public async Task<FolderDto> CreateFolderAsync(string name, string? parentId, CancellationToken ct = default)
    {
        var (res, doc) = await api.SendAsync(HttpMethod.Post, "api/folders",
            new { name, parentFolderId = parentId }, headers: OpHeaders(), ct: ct);
        if (!res.IsSuccessStatusCode) Throw(res, doc);
        return ParseFolder(doc!.RootElement);
    }

    public async Task<FolderDto> UpdateFolderAsync(string id, string name, string? parentId, CancellationToken ct = default)
    {
        var (res, doc) = await api.SendAsync(HttpMethod.Put, $"api/folders/{id}",
            new { name, parentFolderId = parentId }, headers: OpHeaders(), ct: ct);
        if (!res.IsSuccessStatusCode) Throw(res, doc);
        return ParseFolder(doc!.RootElement);
    }

    public async Task DeleteFolderAsync(string id, CancellationToken ct = default)
    {
        var (res, doc) = await api.SendAsync(HttpMethod.Delete, $"api/folders/{id}", headers: OpHeaders(), ct: ct);
        if (!res.IsSuccessStatusCode && res.StatusCode != HttpStatusCode.NoContent) Throw(res, doc);
    }

    public async Task<FileDto> UploadAsync(Stream fileStream, string fileName, string? folderId, string contentType = "application/octet-stream", CancellationToken ct = default)
    {
        var path = string.IsNullOrEmpty(folderId) ? "api/files/upload" : $"api/files/upload?folderId={Uri.EscapeDataString(folderId)}";
        var (res, doc) = await api.UploadAsync(path, fileStream, fileName, contentType,
            headers: new() { ["X-Operation-Id"] = NewOpId() }, ct: ct);
        if (!res.IsSuccessStatusCode) Throw(res, doc);
        return ParseFile(doc!.RootElement);
    }

    public async Task<FileDto> UpdateFileAsync(string id, string name, string? folderId, CancellationToken ct = default)
    {
        var (res, doc) = await api.SendAsync(HttpMethod.Put, $"api/files/{id}",
            new { name, folderId }, headers: OpHeaders(), ct: ct);
        if (!res.IsSuccessStatusCode) Throw(res, doc);
        return ParseFile(doc!.RootElement);
    }

    public async Task DeleteFileAsync(string id, CancellationToken ct = default)
    {
        var (res, doc) = await api.SendAsync(HttpMethod.Delete, $"api/files/{id}", headers: OpHeaders(), ct: ct);
        if (!res.IsSuccessStatusCode && res.StatusCode != HttpStatusCode.NoContent) Throw(res, doc);
    }

    public async Task<string> GetDownloadPathAsync(string fileId, string fileName, CancellationToken ct = default)
    {
        var url = $"{ApiConfig.BaseUrl}/files/{fileId}/download";
        var token = SecureStore.GetAccessToken();
        var req = new HttpRequestMessage(HttpMethod.Get, url);
        if (!string.IsNullOrEmpty(token))
            req.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
        var dev = SecureStore.CurrentDeviceId;
        if (!string.IsNullOrEmpty(dev))
            req.Headers.Add("X-Device-Id", dev);
        using var client = new HttpClient();
        var res = await client.SendAsync(req, ct);
        res.EnsureSuccessStatusCode();
        var dest = Path.Combine(FileSystem.CacheDirectory, fileName);
        await using var src = await res.Content.ReadAsStreamAsync(ct);
        await using var dst = File.Create(dest);
        await src.CopyToAsync(dst, ct);
        return dest;
    }

    private static FolderDto ParseFolder(JsonElement el)
        => new(
            el.GetProperty("id").GetString()!,
            el.GetProperty("name").GetString()!,
            el.TryGetProperty("parentFolderId", out var p) && p.ValueKind != JsonValueKind.Null ? p.GetString() : null,
            el.GetProperty("createdAt").GetString()!,
            el.GetProperty("updatedAt").GetString()!,
            el.TryGetProperty("originDeviceId", out var o) && o.ValueKind != JsonValueKind.Null ? o.GetString() : null);

    private static FileDto ParseFile(JsonElement el)
        => new(
            el.GetProperty("id").GetString()!,
            el.GetProperty("name").GetString()!,
            el.TryGetProperty("folderId", out var p) && p.ValueKind != JsonValueKind.Null ? p.GetString() : null,
            el.GetProperty("hash").GetString()!,
            el.GetProperty("size").GetInt64(),
            el.GetProperty("contentType").GetString()!,
            el.GetProperty("createdAt").GetString()!,
            el.GetProperty("updatedAt").GetString()!,
            el.TryGetProperty("originDeviceId", out var o) && o.ValueKind != JsonValueKind.Null ? o.GetString() : null);

    private static void Throw(HttpResponseMessage res, JsonDocument? doc)
    {
        string? msg = null;
        if (doc is not null)
            try
            {
                if (doc.RootElement.TryGetProperty("detail", out var d)) msg = d.GetString();
                else if (doc.RootElement.TryGetProperty("message", out var m)) msg = m.GetString();
            }
            catch { }
        msg ??= $"Request failed ({(int)res.StatusCode}).";
        throw new InvalidOperationException(msg);
    }
}
