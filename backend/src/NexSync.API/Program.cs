using System.Net;
using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.IdentityModel.Tokens;
using NexSync.API.Extensions;
using NexSync.API.Middleware;
using NexSync.API.Security;
using Scalar.AspNetCore;

var builder = WebApplication.CreateBuilder(args);

// Forwarded Headers: let ASP.NET Core trust X-Forwarded-For/Proto/Host from reverse proxies (ngrok, etc.).
// Without this, Request.Scheme stays "http" behind a TLS-terminating proxy, breaking OpenAPI server URLs,
// cookie Secure flags, and HTTPS redirects.
builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto | ForwardedHeaders.XForwardedHost;
    // Only trust proxies on loopback (ngrok runs locally, production proxies should be listed explicitly).
    options.KnownIPNetworks.Clear();
    options.KnownProxies.Clear();
    options.KnownProxies.Add(IPAddress.Loopback);
    options.KnownProxies.Add(IPAddress.IPv6Loopback);
});

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

// Add rate limiter: max 5 requests per 60 seconds per IP on auth endpoints (configurable for dev/tests)
var maxAuthRequests = builder.Configuration.GetValue<int?>("RateLimiting:MaxRequests") ?? 60;
var authWindowSeconds = builder.Configuration.GetValue<int?>("RateLimiting:WindowSeconds") ?? 60;
builder.Services.AddRateLimiter(maxRequests: maxAuthRequests, windowSeconds: authWindowSeconds);

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
    .WithExposedHeaders("X-CSRF-Token")
    .AllowCredentials()));

builder.Services.AddInfrastructure(builder.Configuration);
builder.Services.AddApplication();

var app = builder.Build();

// ForwardedHeaders MUST run before anything that reads Request.Scheme or RemoteIpAddress.
app.UseForwardedHeaders();

// Security middleware pipeline (order matters)
app.UseMiddleware<ErrorHandlingMiddleware>();
app.UseCors("WebClient");                         // CORS before rate limit so OPTIONS preflight returns immediately
app.UseMiddleware<RateLimitingMiddleware>();       // Rate limit before CSRF check
app.UseSession();                                 // Session must run before CsrfMiddleware (it reads context.Session)
app.UseMiddleware<CsrfMiddleware>();              // CSRF token generation
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

