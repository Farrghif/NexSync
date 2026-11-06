using NexSync.API.Security;
using NexSync.Domain.Exceptions;

namespace NexSync.API.Middleware;

/// <summary>
/// Rate limiting middleware for authentication endpoints.
/// Protects against brute force attacks.
/// </summary>
public class RateLimitingMiddleware
{
    private readonly RequestDelegate _next;
    private readonly RateLimiter _rateLimiter;
    private static readonly string[] ProtectedPaths = { "/api/auth/login", "/api/auth/register", "/api/auth/refresh" };

    public RateLimitingMiddleware(RequestDelegate next, RateLimiter rateLimiter)
    {
        _next = next;
        _rateLimiter = rateLimiter;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        var path = context.Request.Path.Value?.ToLowerInvariant() ?? "";
        
        // Check if this is a protected endpoint
        if (ProtectedPaths.Any(p => path.StartsWith(p, StringComparison.OrdinalIgnoreCase)))
        {
            var clientIp = GetClientIpAddress(context);
            
            if (!_rateLimiter.IsAllowed(clientIp))
            {
                context.Response.StatusCode = StatusCodes.Status429TooManyRequests;
                context.Response.ContentType = "application/problem+json";
                
                var problem = new
                {
                    type = "https://nexsync.dev/errors/rate-limit-exceeded",
                    title = "Too Many Requests",
                    status = 429,
                    detail = "Rate limit exceeded. Please try again later.",
                    code = "RATE_LIMIT_EXCEEDED",
                    traceId = context.TraceIdentifier
                };
                
                await context.Response.WriteAsJsonAsync(problem);
                return;
            }
        }

        await _next(context);
    }

    private static string GetClientIpAddress(HttpContext context)
    {
        // Check for X-Forwarded-For header (proxy scenario)
        if (context.Request.Headers.TryGetValue("X-Forwarded-For", out var forwardedFor))
        {
            var ips = forwardedFor.ToString().Split(',');
            if (ips.Length > 0)
                return ips[0].Trim();
        }

        // Check for X-Real-IP header
        if (context.Request.Headers.TryGetValue("X-Real-IP", out var realIp))
            return realIp.ToString();

        // Fall back to remote IP
        return context.Connection.RemoteIpAddress?.ToString() ?? "unknown";
    }
}
