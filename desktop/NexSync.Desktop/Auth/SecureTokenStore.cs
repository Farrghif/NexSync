using System;
using System.IO;
using System.Security.Cryptography;

namespace NexSync.Desktop.Auth;

public sealed class SecureTokenStore(string filePath)
{
    private readonly string _filePath = filePath;

    public void Save(string refreshToken)
    {
        var dir = Path.GetDirectoryName(_filePath);
        if (!string.IsNullOrEmpty(dir))
            Directory.CreateDirectory(dir);
        var protectedBytes = ProtectedData.Protect(
            System.Text.Encoding.UTF8.GetBytes(refreshToken),
            null,
            DataProtectionScope.CurrentUser);
        var tempPath = _filePath + ".tmp-" + Guid.NewGuid().ToString("N");
        try
        {
            File.WriteAllBytes(tempPath, protectedBytes);
            File.Move(tempPath, _filePath, overwrite: true);
        }
        finally
        {
            if (File.Exists(tempPath))
            {
                try { File.Delete(tempPath); } catch (IOException) { }
            }
        }
    }

    public string? Load()
    {
        if (!File.Exists(_filePath))
            return null;
        var protectedBytes = File.ReadAllBytes(_filePath);
        var raw = ProtectedData.Unprotect(protectedBytes, null, DataProtectionScope.CurrentUser);
        return System.Text.Encoding.UTF8.GetString(raw);
    }

    public void Clear()
    {
        if (File.Exists(_filePath))
            File.Delete(_filePath);
    }
}
