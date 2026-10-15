namespace NexSync.Android.Services;

public static class ApiConfig
{
    private const string Key = "nexsync_api_base";

#if ANDROID
    public const string DefaultBaseUrl = "http://10.0.2.2:5189/api";
#else
    public const string DefaultBaseUrl = "http://localhost:5189/api";
#endif

    public static string BaseUrl
    {
        get => Preferences.Default.Get(Key, DefaultBaseUrl);
        set => Preferences.Default.Set(Key, value.TrimEnd('/'));
    }

    public static string DisplayBaseUrl => BaseUrl;
}
