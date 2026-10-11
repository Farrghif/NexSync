using System;

namespace NexSync.Desktop.Data;

public static class LocalPath
{
    public static string Canonicalize(string relativePath)
    {
        if (string.IsNullOrWhiteSpace(relativePath))
            throw new ArgumentException("Relative path must not be empty.", nameof(relativePath));
        var unified = relativePath.Replace('\\', '/');
        var segments = unified.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (segments.Length == 0)
            throw new ArgumentException("Relative path must not be empty.", nameof(relativePath));
        foreach (var segment in segments)
        {
            if (segment == "." || segment == "..")
                throw new ArgumentException($"Path segment '{segment}' is not allowed in canonical paths.", nameof(relativePath));
            if (segment.EndsWith(' ') || segment.EndsWith('.'))
                throw new ArgumentException($"Path segment '{segment}' must not end with space or dot.", nameof(relativePath));
        }
        return string.Join('/', segments);
    }
}
