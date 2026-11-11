using System.Collections.Concurrent;

namespace NexSync.API.Security;

/// <summary>
/// Simple in-memory rate limiter for authentication endpoints.
/// Limits requests per IP address over a sliding time window.
/// </summary>
public class RateLimiter
{
    private readonly ConcurrentDictionary<string, RequestBucket> _buckets = new();
    private readonly int _maxRequests;
    private readonly TimeSpan _window;

    public RateLimiter(int maxRequests = 5, int windowSeconds = 60)
    {
        _maxRequests = maxRequests;
        _window = TimeSpan.FromSeconds(windowSeconds);
    }

    /// <summary>
    /// Check if request from IP is allowed. Returns true if allowed, false if rate limited.
    /// </summary>
    public bool IsAllowed(string clientIp)
    {
        if (string.IsNullOrEmpty(clientIp))
            clientIp = "unknown";

        var now = DateTime.UtcNow;
        var allowed = true;
        _buckets.AddOrUpdate(clientIp,
            new RequestBucket { FirstRequestTime = now, Count = 1 },
            (_, existing) =>
            {
                var timeSinceFirstRequest = now - existing.FirstRequestTime;
                
                // Reset bucket if outside window
                if (timeSinceFirstRequest > _window)
                    return new RequestBucket { FirstRequestTime = now, Count = 1 };

                // Deny once the window count reached the limit — check BEFORE incrementing,
                // otherwise the capped count can never signal rejection (fails closed under contention).
                if (existing.Count >= _maxRequests)
                {
                    allowed = false;
                    return existing;
                }

                existing.Count++;
                return existing;
            });

        return allowed;
    }

    /// <summary>
    /// Get current request count for IP (for testing/monitoring).
    /// </summary>
    public int GetCurrentCount(string clientIp)
    {
        if (_buckets.TryGetValue(clientIp, out var bucket))
        {
            var timeSinceFirstRequest = DateTime.UtcNow - bucket.FirstRequestTime;
            if (timeSinceFirstRequest > _window)
                return 0;
            return bucket.Count;
        }
        return 0;
    }

    private class RequestBucket
    {
        public DateTime FirstRequestTime { get; set; }
        public int Count { get; set; }
    }
}

/// <summary>
/// Extension to provide singleton rate limiter for DI container.
/// </summary>
public static class RateLimiterExtensions
{
    public static IServiceCollection AddRateLimiter(
        this IServiceCollection services,
        int maxRequests = 5,
        int windowSeconds = 60)
    {
        services.AddSingleton(new RateLimiter(maxRequests, windowSeconds));
        return services;
    }
}
