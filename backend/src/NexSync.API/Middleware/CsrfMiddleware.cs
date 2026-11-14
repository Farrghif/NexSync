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

        // Snapshot only as a fallback; ensure the session has a token pre-controller.
        var token = CsrfTokenService.GetOrCreateToken(context.Session);

        // Read the session's *current* token at response start: login/register/refresh
        // rotate it mid-request, so a pre-controller snapshot hands header-reading
        // clients a stale token and their next refresh fails with "Invalid CSRF token".
        // Error responses are written after the pipeline unwinds past SessionMiddleware
        // (session throws there); those paths never rotate, so the snapshot is current.
        context.Response.OnStarting(() =>
        {
            if (context.Response.Headers.ContainsKey(CsrfTokenHeader))
                return Task.CompletedTask;

            string current;
            try
            {
                current = CsrfTokenService.GetOrCreateToken(context.Session);
            }
            catch (InvalidOperationException)
            {
                current = token;
            }

            context.Response.Headers[CsrfTokenHeader] = current;
            return Task.CompletedTask;
        });

        await _next(context);
        
        // Save session after request processing
        await context.Session.CommitAsync();
    }
}
