using System.Text;
using System.Text.Json;

namespace NexSync.Android.Services;

public sealed class AuthService(ApiClient api)
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public string? CurrentUserId => SecureStore.CurrentUserId;
    public string? CurrentEmail => SecureStore.CurrentEmail;
    public string? CurrentFullName => SecureStore.CurrentFullName;
    public bool IsSignedIn => CurrentUserId is not null && !string.IsNullOrEmpty(api.AccessToken);

    public static string DeviceRevokedMessage =>
        "This device has been revoked. Please enroll this device again to continue.";

    public async Task RegisterAsync(string email, string password, string fullName, CancellationToken ct = default)
    {
        var (res, doc) = await api.SendAsync(HttpMethod.Post, "api/auth/register",
            new { email, password, fullName }, allowRefresh: false, ct: ct);
        if (!res.IsSuccessStatusCode)
            ThrowFriendly(res, doc);
        ApplyAuthResult(doc!);
        await EnsureAndroidDeviceAsync(ct);
    }

    public async Task LoginAsync(string email, string password, CancellationToken ct = default)
    {
        var (res, doc) = await api.SendAsync(HttpMethod.Post, "api/auth/login",
            new { email, password }, allowRefresh: false, ct: ct);
        if (!res.IsSuccessStatusCode)
            ThrowFriendly(res, doc);
        ApplyAuthResult(doc!);
        await EnsureAndroidDeviceAsync(ct);
    }

    public async Task LogoutAsync(CancellationToken ct = default)
    {
        try
        {
            var (res, _) = await api.SendAsync(HttpMethod.Post, "api/auth/logout",
                null, headers: new() { ["X-CSRF"] = "1" }, allowRefresh: false, ct: ct);
            _ = res;
        }
        catch { }
        finally
        {
            api.ClearAuth();
            SecureStore.ClearSession();
        }
    }

    private void ApplyAuthResult(JsonDocument doc)
    {
        var root = doc.RootElement;
        var accessToken = root.GetProperty("accessToken").GetString()!;
        var user = root.GetProperty("user");
        var id = user.GetProperty("id").GetString()!;
        var email = user.GetProperty("email").GetString()!;
        var fullName = user.GetProperty("fullName").GetString() ?? email;
        api.SetAccessToken(accessToken);
        SecureStore.SaveSession(accessToken, id, email, fullName);
    }

    private static string AndroidDeviceName
    {
        get
        {
            var model = DeviceInfo.Current.Model;
            var manu = DeviceInfo.Current.Manufacturer;
            var name = $"{manu} {model}".Trim();
            return string.IsNullOrWhiteSpace(name) ? "Android Device" : name;
        }
    }

    public async Task<string> EnsureAndroidDeviceAsync(CancellationToken ct = default)
    {
        var userId = CurrentUserId ?? throw new InvalidOperationException("Not signed in.");
        var existing = SecureStore.GetDeviceId(userId);
        if (!string.IsNullOrEmpty(existing))
        {
            try
            {
                var (res, doc) = await api.SendAsync(HttpMethod.Get, "api/devices", allowRefresh: true, ct: ct);
                if (res.IsSuccessStatusCode && doc is not null)
                {
                    foreach (var item in doc.RootElement.EnumerateArray())
                    {
                        if (item.TryGetProperty("deviceId", out var idEl)
                            && idEl.GetString() == existing
                            && item.TryGetProperty("status", out var stEl)
                            && stEl.GetString() == "ACTIVE")
                            return existing!;
                        if (item.TryGetProperty("deviceId", out var id2)
                            && id2.GetString() == existing)
                        {
                            SecureStore.RemoveDeviceId(userId);
                            throw new InvalidOperationException(DeviceRevokedMessage);
                        }
                    }
                    return existing!;
                }
            }
            catch (InvalidOperationException)
            {
                throw;
            }
            catch { return existing!; }
        }
        return await EnrollAndroidDeviceAsync(ct);
    }

    public async Task<string> EnrollAndroidDeviceAsync(CancellationToken ct = default)
    {
        var userId = CurrentUserId ?? throw new InvalidOperationException("Not signed in.");
        var opId = SecureStore.GetDeviceOp(userId);
        if (string.IsNullOrEmpty(opId))
        {
            opId = Guid.NewGuid().ToString();
            SecureStore.SetDeviceOp(userId, opId);
        }
        var (res, doc) = await api.SendAsync(HttpMethod.Post, "api/devices",
            new { name = AndroidDeviceName, platform = "Android" },
            headers: new() { ["X-Operation-Id"] = opId }, ct: ct);
        if (!res.IsSuccessStatusCode)
            ThrowFriendly(res, doc);
        var deviceId = doc!.RootElement.GetProperty("deviceId").GetString()!;
        SecureStore.SetDeviceId(userId, deviceId);
        return deviceId;
    }

    private static void ThrowFriendly(HttpResponseMessage res, JsonDocument? doc)
    {
        string? detail = null;
        if (doc is not null)
        {
            try
            {
                if (doc.RootElement.TryGetProperty("detail", out var d)) detail = d.GetString();
                else if (doc.RootElement.TryGetProperty("message", out var m)) detail = m.GetString();
            }
            catch { }
        }
        detail ??= $"Request failed ({(int)res.StatusCode}).";
        throw new InvalidOperationException(detail);
    }
}
