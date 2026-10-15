namespace NexSync.Android.Services;

public static class SecureStore
{
    private const string AccessTokenKey = "nexsync_access_token";
    private const string UserIdKey = "nexsync_user_id";
    private const string UserJsonKey = "nexsync_user_json";
    private const string DeviceIdKey = "nexsync_device_id";
    private const string DeviceOpKey = "nexsync_device_op";
    private const string UserEmailKey = "nexsync_user_email";
    private const string UserNameKey = "nexsync_user_fullname";

    private static string DeviceKeyFor(string userId) => $"{DeviceIdKey}:{userId}";
    private static string DeviceOpKeyFor(string userId) => $"{DeviceOpKey}:{userId}";

    public static string? GetAccessToken()
        => SecureStorage.GetAsync(AccessTokenKey).GetAwaiter().GetResult();

    public static void SetAccessToken(string? value)
    {
        if (value is null)
            SecureStorage.Remove(AccessTokenKey);
        else
            SecureStorage.SetAsync(AccessTokenKey, value).GetAwaiter().GetResult();
    }

    public static void SaveSession(
        string accessToken, string userId, string email, string fullName)
    {
        SetAccessToken(accessToken);
        Preferences.Default.Set(UserIdKey, userId);
        Preferences.Default.Set(UserEmailKey, email);
        Preferences.Default.Set(UserNameKey, fullName);
        Preferences.Default.Set(UserJsonKey,
            System.Text.Json.JsonSerializer.Serialize(new { id = userId, email, fullName }));
    }

    public static void ClearSession()
    {
        SetAccessToken(null);
        Preferences.Default.Remove(UserIdKey);
        Preferences.Default.Remove(UserJsonKey);
        Preferences.Default.Remove(UserEmailKey);
        Preferences.Default.Remove(UserNameKey);
    }

    public static void ClearAll()
    {
        ClearSession();
    }

    public static string? CurrentUserId => Preferences.Default.Get<string?>(UserIdKey, null);
    public static string? CurrentEmail => Preferences.Default.Get<string?>(UserEmailKey, null);
    public static string? CurrentFullName => Preferences.Default.Get<string?>(UserNameKey, null);

    public static string? GetDeviceId(string userId)
        => Preferences.Default.Get<string?>(DeviceKeyFor(userId), null);

    public static void SetDeviceId(string userId, string deviceId)
        => Preferences.Default.Set(DeviceKeyFor(userId), deviceId);

    public static void RemoveDeviceId(string userId)
        => Preferences.Default.Remove(DeviceKeyFor(userId));

    public static string? GetDeviceOp(string userId)
        => Preferences.Default.Get<string?>(DeviceOpKeyFor(userId), null);

    public static void SetDeviceOp(string userId, string opId)
        => Preferences.Default.Set(DeviceOpKeyFor(userId), opId);

    public static string? CurrentDeviceId
    {
        get
        {
            var uid = CurrentUserId;
            return uid is null ? null : GetDeviceId(uid);
        }
    }
}
