using NexSync.API.Security;

namespace NexSync.API.Middleware;

/// <summary>
/// Middleware to generate and manage CSRF tokens for each session.
/// </summary>
public class CsrfMiddleware
{
    private readonly RequestDelegate _next;
    private const string CsrfTokenHeader = "X-CSRF-Token";

    public CsrfMiddleware(RequestDelegate next)
    {
        _next = next;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        // Enable session if not already enabled
        await context.Session.LoadAsync();

        // Generate or retrieve the session's CSRF token once per request.
        // Regenerating on every request would invalidate the token the client holds,
        // causing refresh/logout validation to always fail (Invalid CSRF token).
        var token = CsrfTokenService.GetOrCreateToken(context.Session);

        // Add CSRF token to response header for client to use in subsequent requests
        context.Response.OnStarting(() =>
        {
            if (!context.Response.Headers.ContainsKey(CsrfTokenHeader))
                context.Response.Headers[CsrfTokenHeader] = token;
            return Task.CompletedTask;
        });

        await _next(context);
        
        // Save session after request processing
        await context.Session.CommitAsync();
    }
}
