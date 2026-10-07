using System.Net;
using System.Text.Json;
using NexSync.Domain.Exceptions;

namespace NexSync.API.Middleware;

public class ErrorHandlingMiddleware(RequestDelegate next, ILogger<ErrorHandlingMiddleware> logger)
{
    public async Task InvokeAsync(HttpContext context)
    {
        try { await next(context); }
        catch (Exception ex)
        {
            logger.LogError(ex, "Unhandled exception");
            await HandleAsync(context, ex);
        }
    }

    private static Task HandleAsync(HttpContext ctx, Exception ex)
    {
        var (status, code, title) = ex switch
        {
            NexSync.Domain.Exceptions.UnauthorizedAccessException => (HttpStatusCode.Unauthorized, "UNAUTHORIZED", "Unauthorized"),
            ForbiddenException => (HttpStatusCode.Forbidden, "FORBIDDEN", "Forbidden"),
            NotFoundException => (HttpStatusCode.NotFound, "NOT_FOUND", "Not found"),
            OperationIdReuseException => (HttpStatusCode.BadRequest, "OPERATION_ID_REUSE", "Operation ID already used for a different request"),
            ConflictException => (HttpStatusCode.Conflict, "CONFLICT", "Conflict"),
            FileNameInvalidException => (HttpStatusCode.BadRequest, "INVALID_NAME", "Invalid name"),
            DomainException => (HttpStatusCode.BadRequest, "DOMAIN_ERROR", "Bad request"),
            InvalidOperationException => (HttpStatusCode.RequestEntityTooLarge, "FILE_TOO_LARGE", "File too large"),
            _ => (HttpStatusCode.InternalServerError, "INTERNAL_ERROR", "Internal server error")
        };
        ctx.Response.ContentType = "application/problem+json";
        ctx.Response.StatusCode = (int)status;
        var problem = new
        {
            type = $"https://nexsync.dev/errors/{code.ToLower().Replace('_', '-')}",
            title,
            status = (int)status,
            detail = status == HttpStatusCode.InternalServerError ? "An unexpected error occurred." : ex.Message,
            code,
            traceId = ctx.TraceIdentifier
        };
        return ctx.Response.WriteAsync(JsonSerializer.Serialize(problem));
    }
}
