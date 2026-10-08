using System.Security.Cryptography;
using System.Text;

namespace NexSync.Domain.Sync;

public static class RequestFingerprint
{
    public static string ForDeviceRegistration(string name, string platform)
        => Sha256Hex($"POST:/api/devices:{name}:{platform}");

    public static string ForMutation(string method, string path, params (string Key, string? Value)[] fields)
    {
        var ordered = fields
            .OrderBy(f => f.Key, StringComparer.Ordinal)
            .Select(f => $"{f.Key}={f.Value ?? string.Empty}");
        return Sha256Hex($"{method.ToUpperInvariant()}:{path}|{string.Join("|", ordered)}");
    }

    public static string Sha256Hex(string canonical)
        => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical))).ToLowerInvariant();
}
