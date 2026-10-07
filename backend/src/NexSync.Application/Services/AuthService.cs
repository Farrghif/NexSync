using NexSync.Application.DTOs;
using NexSync.Application.Interfaces;
using NexSync.Application.Validators;
using NexSync.Domain.Entities;
using NexSync.Domain.Exceptions;

namespace NexSync.Application.Services;

public class AuthService(
    IUserRepository users,
    ITokenService tokens,
    IAccessTokenProvider provider,
    IPasswordHasher hasher) : IAuthService
{
    public async Task<AuthResponse> RegisterAsync(RegisterRequest request)
    {
        AuthValidator.ValidateRegister(request.Email, request.Password, request.FullName);
        if (await users.EmailExistsAsync(request.Email))
            throw new ConflictException("Email already registered.");
        var user = new User
        {
            Id = Guid.NewGuid(),
            Email = request.Email.ToLower(),
            PasswordHash = hasher.HashPassword(request.Password),
            FullName = request.FullName,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };
        await users.AddAsync(user);
        var created = await tokens.CreateRefreshTokenAsync(user.Id);
        var access = provider.GenerateAccessToken(user.Id, user.Email);
        return new AuthResponse(access, created.RawToken, new UserDto(user.Id, user.Email, user.FullName));
    }

    public async Task<AuthResponse> LoginAsync(LoginRequest request)
    {
        var user = await users.GetByEmailAsync(request.Email);
        if (user is null || !hasher.VerifyPassword(request.Password, user.PasswordHash))
            throw new Domain.Exceptions.UnauthorizedAccessException("Invalid credentials.");
        var created = await tokens.CreateRefreshTokenAsync(user.Id);
        var access = provider.GenerateAccessToken(user.Id, user.Email);
        return new AuthResponse(access, created.RawToken, new UserDto(user.Id, user.Email, user.FullName));
    }

    public async Task<AuthResponse> RefreshTokenAsync(RefreshTokenRequest request)
    {
        var existing = await tokens.ValidateRefreshTokenAsync(request.RefreshToken);
        if (existing is null)
        {
            var stale = await tokens.FindByRawTokenAsync(request.RefreshToken);
            if (stale is not null)
                await tokens.RevokeTokenFamilyAsync(stale.Id);
            throw new Domain.Exceptions.UnauthorizedAccessException("Invalid refresh token.");
        }
        var user = await users.GetByIdAsync(existing.UserId)
            ?? throw new Domain.Exceptions.UnauthorizedAccessException("User not found.");
        var created = await tokens.CreateRefreshTokenAsync(user.Id);
        await tokens.LinkRotationAsync(existing.Id, created.Token.Id);
        var accessToken = provider.GenerateAccessToken(user.Id, user.Email);
        return new AuthResponse(accessToken, created.RawToken, new UserDto(user.Id, user.Email, user.FullName));
    }

    public async Task LogoutAsync(LogoutRequest request)
    {
        var token = await tokens.ValidateRefreshTokenAsync(request.RefreshToken);
        if (token is not null)
            await tokens.RevokeRefreshTokenAsync(token.Id);
    }
}
