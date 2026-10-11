using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;

namespace NexSync.Desktop.Sync;

public sealed class SuppressionTracker : IRemoteWriteTracker
{
    public static readonly TimeSpan DefaultTtl = TimeSpan.FromSeconds(10);

    private readonly TimeSpan _ttl;
    private readonly ConcurrentDictionary<string, SuppressionEntry> _entries = new(StringComparer.OrdinalIgnoreCase);

    public SuppressionTracker(TimeSpan? ttl = null)
    {
        _ttl = ttl ?? DefaultTtl;
    }

    public IDisposable Suppress(string relPath, string? expectedHash)
    {
        var key = CanonicalKey(relPath);
        _entries.AddOrUpdate(key,
            _ => new SuppressionEntry(expectedHash, 1, DateTime.UtcNow + _ttl),
            (_, existing) => existing with
            {
                ExpectedHash = expectedHash,
                RefCount = existing.RefCount + 1,
                ExpiresAt = DateTime.UtcNow + _ttl
            });
        return new Scope(this, key);
    }

    public bool IsSuppressed(string relPath, string? currentHash)
    {
        var key = CanonicalKey(relPath);
        if (!_entries.TryGetValue(key, out var entry))
        {
            SweepExpired();
            return false;
        }
        if (entry.RefCount > 0)
            return true;
        if (entry.ExpiresAt < DateTime.UtcNow)
        {
            _entries.TryRemove(new KeyValuePair<string, SuppressionEntry>(key, entry));
            return false;
        }
        if (entry.ExpectedHash is null || currentHash is null)
            return entry.ExpectedHash is null;
        return string.Equals(entry.ExpectedHash, currentHash, StringComparison.OrdinalIgnoreCase);
    }

    private int _sweepCounter;

    private void SweepExpired()
    {
        if (Interlocked.Increment(ref _sweepCounter) % 64 != 0)
            return;
        var now = DateTime.UtcNow;
        foreach (var pair in _entries)
        {
            if (pair.Value.RefCount == 0 && pair.Value.ExpiresAt < now)
                _entries.TryRemove(new KeyValuePair<string, SuppressionEntry>(pair.Key, pair.Value));
        }
    }

    private void Release(string key)
    {
        while (true)
        {
            if (!_entries.TryGetValue(key, out var entry))
                return;
            var updated = entry with { RefCount = Math.Max(0, entry.RefCount - 1) };
            if (_entries.TryUpdate(key, updated, entry))
                return;
        }
    }

    private static string CanonicalKey(string relPath) =>
        relPath.Replace('\\', '/').Trim('/');

    private sealed record SuppressionEntry(string? ExpectedHash, int RefCount, DateTime ExpiresAt);

    private sealed class Scope(SuppressionTracker owner, string key) : IDisposable
    {
        private bool _disposed;
        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            owner.Release(key);
        }
    }
}
