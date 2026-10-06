using Microsoft.AspNetCore.Mvc;
using NexSync.Domain.Exceptions;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace NexSync.API.Middleware;

public class ErrorHandlingMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<ErrorHandlingMiddleware> _logger;

    public ErrorHandlingMiddleware(RequestDelegate next, ILogger<ErrorHandlingMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await _next(context);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "An unhandled exception occurred");
            await HandleExceptionAsync(context, ex);
        }
    }

    private static Task HandleExceptionAsync(HttpContext context, Exception exception)
    {
        context.Response.ContentType = "application/json";

        var (statusCode, errorCode, title) = GetErrorDetails(exception);
        
        var problemDetails = new
        {
            type = $"https://nexsync.dev/errors/{errorCode}",
            title = title,
            status = statusCode,
            detail = exception.Message,
            code = errorCode
        };

        var isDevelopment = context.RequestServices.GetService<IHostEnvironment>()?.IsDevelopment() == true;
        
        object responseObject = isDevelopment
            ? new { problemDetails, traceId = context.TraceIdentifier }
            : problemDetails;

        context.Response.StatusCode = statusCode;
        var json = JsonSerializer.Serialize(responseObject, new JsonSerializerOptions 
        { 
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
        });
        
        return context.Response.WriteAsync(json);
    }

    private static (int statusCode, string errorCode, string title) GetErrorDetails(Exception exception)
    {
        return exception switch
        {
            NexSync.Domain.Exceptions.UnauthorizedAccessException => (401, "unauthorized-access", "Unauthorized"),
            ConflictException => (409, "conflict", "Conflict"),
            _ => (500, exception.GetType().Name.ToKebabCase(), "Internal Server Error")
        };
    }
}

file static class StringExtensions
{
    public static string ToKebabCase(this string str)
    {
        if (string.IsNullOrEmpty(str))
            return str;

        var result = new System.Text.StringBuilder();
        for (int i = 0; i < str.Length; i++)
        {
            if (char.IsUpper(str[i]) && i > 0)
                result.Append('-');
            result.Append(char.ToLowerInvariant(str[i]));
        }
        return result.ToString();
    }
}
