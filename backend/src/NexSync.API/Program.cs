using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using NexSync.API.Extensions;
using NexSync.API.Middleware;
using NexSync.API.Security;
using Scalar.AspNetCore;

var builder = WebApplication.CreateBuilder(args);

// Session backing store required by AddSession.
// shortcut: in-memory only — sessions are lost on restart and not shared across instances; use AddStackExchangeRedisCache when running multi-instance.
builder.Services.AddDistributedMemoryCache();

// Session configuration for CSRF token storage
builder.Services.AddSession(options =>
{
    options.IdleTimeout = TimeSpan.FromMinutes(20);
    options.Cookie.HttpOnly = true;
    options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
    options.Cookie.SameSite = SameSiteMode.Strict;
    options.Cookie.IsEssential = true;
});

builder.Services.AddControllers();
builder.Services.AddOpenApi();

// Add rate limiter: max 5 requests per 60 seconds per IP on auth endpoints
builder.Services.AddRateLimiter(maxRequests: 5, windowSeconds: 60);

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(o =>
    {
        var key = Encoding.UTF8.GetBytes(builder.Configuration["Jwt:Secret"] ?? "");
        if (key.Length < 32)
            throw new InvalidOperationException(
                "JWT Secret must be at least 32 characters. Set via environment variable 'Jwt__Secret' in production.");
        
        o.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(key),
            ValidateIssuer = true,
            ValidIssuer = builder.Configuration["Jwt:Issuer"],
            ValidateAudience = true,
            ValidAudience = builder.Configuration["Jwt:Audience"],
            ValidateLifetime = true,
            ClockSkew = TimeSpan.Zero
        };
    });

// CORS configuration: explicit methods and headers
builder.Services.AddCors(o => o.AddPolicy("WebClient", p => p
    .WithOrigins(builder.Configuration.GetSection("Cors:Origins").Get<string[]>() ?? ["http://localhost:5173"])
    .WithMethods("GET", "POST", "PUT", "DELETE", "OPTIONS")
    .WithHeaders("Content-Type", "Authorization", "X-Device-Id", "X-Operation-Id", "X-CSRF-Token")
    .AllowCredentials()));

builder.Services.AddInfrastructure(builder.Configuration);
builder.Services.AddApplication();

var app = builder.Build();

// Security middleware pipeline (order matters)
app.UseMiddleware<ErrorHandlingMiddleware>();
app.UseMiddleware<RateLimitingMiddleware>();      // Rate limit before CSRF check
app.UseSession();                                 // Session must run before CsrfMiddleware (it reads context.Session)
app.UseMiddleware<CsrfMiddleware>();              // CSRF token generation
app.UseCors("WebClient");
app.UseAuthentication();
app.UseAuthorization();

// Security headers
app.Use(async (context, next) =>
{
    // Prevent clickjacking
    context.Response.Headers["X-Frame-Options"] = "DENY";
    
    // Prevent MIME type sniffing
    context.Response.Headers["X-Content-Type-Options"] = "nosniff";
    
    // Enable XSS protection (browser-specific)
    context.Response.Headers["X-XSS-Protection"] = "1; mode=block";
    
    // Content Security Policy
    context.Response.Headers["Content-Security-Policy"] = 
        "default-src 'self'; script-src 'self'; style-src 'self' 'unsafe-inline'; img-src 'self' data: https:; font-src 'self';";
    
    // HSTS (HTTP Strict Transport Security) - force HTTPS
    if (app.Environment.IsProduction())
        context.Response.Headers["Strict-Transport-Security"] = "max-age=31536000; includeSubDomains; preload";
    
    // Referrer policy
    context.Response.Headers["Referrer-Policy"] = "strict-origin-when-cross-origin";
    
    await next();
});

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    app.MapScalarApiReference();
}

app.MapControllers();
app.Run();

public partial class Program { }

