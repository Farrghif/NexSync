using Microsoft.AspNetCore.Mvc;
using NexSync.Application.DTOs;
using NexSync.Application.Interfaces;

namespace NexSync.API.Controllers;

[ApiController]
[Route("api/[controller]")]
public class AuthController(IAuthService auth) : ControllerBase
{
    private void SetCookie(string token) => Response.Cookies.Append("refreshToken", token,
        new CookieOptions { HttpOnly = true, Secure = Request.IsHttps, SameSite = SameSiteMode.Strict, Expires = DateTimeOffset.UtcNow.AddDays(7), Path = "/api/auth" });

    [HttpPost("register")]
    public async Task<IActionResult> Register([FromBody] RegisterRequest req)
    {
        var res = await auth.RegisterAsync(req);
        SetCookie(res.RefreshToken);
        return Ok(new { accessToken = res.AccessToken, user = res.User });
    }

    [HttpPost("login")]
    public async Task<IActionResult> Login([FromBody] LoginRequest req)
    {
        var res = await auth.LoginAsync(req);
        SetCookie(res.RefreshToken);
        return Ok(new { accessToken = res.AccessToken, user = res.User });
    }

    [HttpPost("refresh")]
    public async Task<IActionResult> Refresh([FromHeader(Name = "X-CSRF")] string? csrf)
    {
        if (csrf != "1") return BadRequest(new { message = "CSRF header required." });
        var raw = Request.Cookies["refreshToken"];
        if (string.IsNullOrEmpty(raw)) return Unauthorized(new { message = "No refresh token." });
        var res = await auth.RefreshTokenAsync(new RefreshTokenRequest(raw));
        SetCookie(res.RefreshToken);
        return Ok(new { accessToken = res.AccessToken });
    }

    [HttpPost("logout")]
    public async Task<IActionResult> Logout([FromHeader(Name = "X-CSRF")] string? csrf)
    {
        if (csrf != "1") return BadRequest(new { message = "CSRF header required." });
        var raw = Request.Cookies["refreshToken"];
        if (!string.IsNullOrEmpty(raw)) await auth.LogoutAsync(new LogoutRequest(raw));
        Response.Cookies.Delete("refreshToken", new CookieOptions { Path = "/api/auth" });
        return NoContent();
    }
}
