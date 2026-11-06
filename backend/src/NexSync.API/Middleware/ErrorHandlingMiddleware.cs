using System.Net;
using System.Text.Json;
using NexSync.Domain.Exceptions;

namespace NexSync.API.Middleware;

/// <summary>
/// Global error handling middleware with secure error messaging and audit logging.
/// </summary>
public class ErrorHandlingMiddleware(RequestDelegate next, ILogger<ErrorHandlingMiddleware> logger)
{
    public async Task InvokeAsync(HttpContext context)
    {
        try { await next(context); }
        catch (Exception ex)
        {
            logger.LogError(ex, "Unhandled exception in {Path}", context.Request.Path);
            await HandleAsync(context, ex, logger);
        }
    }

    private static Task HandleAsync(HttpContext ctx, Exception ex, ILogger logger)
    {
        var (status, code, title, detail) = ex switch
        {
            NexSync.Domain.Exceptions.UnauthorizedAccessException => 
                (HttpStatusCode.Unauthorized, "UNAUTHORIZED", "Unauthorized", "Authentication required or invalid."),
            
            ForbiddenException => 
                (HttpStatusCode.Forbidden, "FORBIDDEN", "Forbidden", "You do not have permission to access this resource."),
            
            NotFoundException => 
                (HttpStatusCode.NotFound, "NOT_FOUND", "Not found", "The requested resource was not found."),
            
            OperationIdReuseException => 
                (HttpStatusCode.BadRequest, "OPERATION_ID_REUSE", "Invalid request", "This operation has already been processed."),
            
            ConflictException => 
                (HttpStatusCode.Conflict, "CONFLICT", "Conflict", "A resource with this name already exists."),
            
            FileNameInvalidException => 
                (HttpStatusCode.BadRequest, "INVALID_NAME", "Invalid name", "The provided name is invalid."),
            
            DomainException => 
                (HttpStatusCode.BadRequest, "DOMAIN_ERROR", "Bad request", "The request contains invalid data."),
            
            InvalidOperationException when ex.Message.Contains("File exceeds") => 
                (HttpStatusCode.RequestEntityTooLarge, "FILE_TOO_LARGE", "File too large", "The uploaded file exceeds the maximum allowed size."),
            
            InvalidOperationException => 
                (HttpStatusCode.BadRequest, "INVALID_OPERATION", "Invalid operation", "The operation could not be completed."),
            
            ArgumentException or ArgumentNullException => 
                (HttpStatusCode.BadRequest, "INVALID_ARGUMENT", "Invalid argument", "One or more arguments are invalid."),
            
            _ => 
                (HttpStatusCode.InternalServerError, "INTERNAL_ERROR", "Internal server error", "An unexpected error occurred. Please contact support if this persists.")
        };

        // Log security-relevant events
        if (status == HttpStatusCode.Unauthorized || status == HttpStatusCode.Forbidden)
        {
            logger.LogWarning("Security event: {Code} from {RemoteIp} for {Path}",
                code, ctx.Connection.RemoteIpAddress, ctx.Request.Path);
        }

        ctx.Response.ContentType = "application/problem+json";
        ctx.Response.StatusCode = (int)status;
        
        var problem = new
        {
            type = $"https://nexsync.dev/errors/{code.ToLower().Replace('_', '-')}",
            title,
            status = (int)status,
            detail, // Always generic in production, never expose internal details
            code,
            traceId = ctx.TraceIdentifier,
            timestamp = DateTime.UtcNow.ToString("O")
        };

        return ctx.Response.WriteAsJsonAsync(problem);
    }
}
