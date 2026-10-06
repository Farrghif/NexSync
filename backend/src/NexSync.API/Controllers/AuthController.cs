using Microsoft.AspNetCore.Mvc;
using NexSync.Application.DTOs;
using NexSync.Application.Interfaces;

namespace NexSync.API.Controllers;

[ApiController]
[Route("api/[controller]")]
public class AuthController : ControllerBase
{
    private readonly IAuthService _authService;

    public AuthController(IAuthService authService)
    {
        _authService = authService;
    }

    [HttpPost("register")]
    public async Task<IActionResult> Register([FromBody] RegisterRequest request)
    {
        var response = await _authService.RegisterAsync(request);
        SetRefreshTokenCookie(response.RefreshToken);
        return Ok(new { accessToken = response.AccessToken, user = response.User });
    }

    [HttpPost("login")]
    public async Task<IActionResult> Login([FromBody] LoginRequest request)
    {
        var response = await _authService.LoginAsync(request);
        SetRefreshTokenCookie(response.RefreshToken);
        return Ok(new { accessToken = response.AccessToken, user = response.User });
    }

    [HttpPost("refresh")]
    public async Task<IActionResult> Refresh([FromHeader(Name = "X-CSRF")] string? csrf)
    {
        if (csrf != "1")
            return BadRequest(new ProblemDetails
            {
                Type = "https://nexsync.dev/errors/invalid-csrf",
                Title = "Invalid CSRF",
                Status = 400,
                Detail = "X-CSRF header required"
            });

        var refreshToken = Request.Cookies["refreshToken"];
        if (string.IsNullOrEmpty(refreshToken))
            return Unauthorized(new ProblemDetails
            {
                Type = "https://nexsync.dev/errors/missing-refresh-token",
                Title = "Missing Refresh Token",
                Status = 401,
                Detail = "No refresh token cookie found"
            });

        var response = await _authService.RefreshTokenAsync(new RefreshTokenRequest(refreshToken));
        SetRefreshTokenCookie(response.RefreshToken);
        return Ok(new { accessToken = response.AccessToken });
    }

    [HttpPost("logout")]
    public async Task<IActionResult> Logout([FromHeader(Name = "X-CSRF")] string? csrf)
    {
        if (csrf != "1")
            return BadRequest(new ProblemDetails
            {
                Type = "https://nexsync.dev/errors/invalid-csrf",
                Title = "Invalid CSRF",
                Status = 400,
                Detail = "X-CSRF header required"
            });

        var refreshToken = Request.Cookies["refreshToken"];
        if (!string.IsNullOrEmpty(refreshToken))
        {
            await _authService.LogoutAsync(new LogoutRequest(refreshToken));
        }

        Response.Cookies.Delete("refreshToken");
        return NoContent();
    }

    private void SetRefreshTokenCookie(string token)
    {
        Response.Cookies.Append(
            "refreshToken",
            token,
            new CookieOptions
            {
                HttpOnly = true,
                Secure = true,
                SameSite = SameSiteMode.Strict,
                Expires = DateTimeOffset.UtcNow.AddDays(7),
                Path = "/api/auth"
            });
    }
}
