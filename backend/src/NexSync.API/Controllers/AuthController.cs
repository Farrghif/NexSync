using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using NexSync.API.Security;
using NexSync.Application.DTOs;
using NexSync.Application.Interfaces;
using NexSync.Domain.Exceptions;
using NexSync.Domain.Sync;

namespace NexSync.API.Controllers;

[ApiController]
[Route("api/[controller]")]
public class AuthController(IAuthService auth) : ControllerBase
{
    private void SetCookie(string token) => Response.Cookies.Append("refreshToken", token,
        new CookieOptions 
        { 
            HttpOnly = true, 
            Secure = Request.IsHttps, 
            SameSite = SameSiteMode.Strict, 
            Expires = DateTimeOffset.UtcNow.AddDays(7), 
            Path = "/api"
        });

    [HttpPost("register")]
    public async Task<IActionResult> Register([FromBody] RegisterRequest req)
    {
        var res = await auth.RegisterAsync(req);
        SetCookie(res.RefreshToken);
        
        var csrfToken = CsrfTokenService.GenerateToken(HttpContext.Session);
        
        return Ok(new 
        { 
            accessToken = res.AccessToken, 
            user = res.User,
            csrfToken
        });
    }

    [HttpPost("login")]
    public async Task<IActionResult> Login([FromBody] LoginRequest req)
    {
        var res = await auth.LoginAsync(req);
        SetCookie(res.RefreshToken);
        
        var csrfToken = CsrfTokenService.GenerateToken(HttpContext.Session);
        
        return Ok(new 
        { 
            accessToken = res.AccessToken, 
            user = res.User,
            csrfToken
        });
    }

    [HttpPost("refresh")]
    public async Task<IActionResult> Refresh([FromHeader(Name = "X-CSRF-Token")] string? csrfToken)
    {
        if (!CsrfTokenService.ValidateToken(HttpContext.Session, csrfToken))
            throw new NexSync.Domain.Exceptions.UnauthorizedAccessException("Invalid CSRF token.");
        
        var raw = Request.Cookies["refreshToken"];
        if (string.IsNullOrEmpty(raw)) 
            throw new NexSync.Domain.Exceptions.UnauthorizedAccessException("No refresh token provided.");
        
        var res = await auth.RefreshTokenAsync(new RefreshTokenRequest(raw));
        SetCookie(res.RefreshToken);
        
        var newCsrfToken = CsrfTokenService.GenerateToken(HttpContext.Session);
        
        return Ok(new 
        { 
            accessToken = res.AccessToken,
            csrfToken = newCsrfToken
        });
    }

    [HttpPost("logout")]
    public async Task<IActionResult> Logout([FromHeader(Name = "X-CSRF-Token")] string? csrfToken)
    {
        if (!CsrfTokenService.ValidateToken(HttpContext.Session, csrfToken))
            throw new NexSync.Domain.Exceptions.UnauthorizedAccessException("Invalid CSRF token.");
        
        var raw = Request.Cookies["refreshToken"];
        if (!string.IsNullOrEmpty(raw)) 
            await auth.LogoutAsync(new LogoutRequest(raw));
        
        Response.Cookies.Delete("refreshToken", new CookieOptions { Path = "/api" });
        return NoContent();
    }
}
