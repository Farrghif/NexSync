using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Configuration;
using NexSync.Application.Interfaces;
using NexSync.Domain.Entities;

namespace NexSync.Application.Services;

public class TokenService(IRefreshTokenRepository repo, IConfiguration config) : ITokenService
{
    private readonly IRefreshTokenRepository _repo = repo;
    private readonly IConfiguration _config = config;

    public async Task<(RefreshToken Token, string RawToken)> CreateRefreshTokenAsync(Guid userId)
    {
        var raw = Convert.ToBase64String(RandomNumberGenerator.GetBytes(64));
        var token = new RefreshToken
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            TokenHash = HashToken(raw),
            ExpiresAt = DateTime.UtcNow.AddDays(double.Parse(_config["Jwt:RefreshTokenExpirationDays"] ?? "7")),
            CreatedAt = DateTime.UtcNow
        };
        await _repo.AddAsync(token);
        return (token, raw);
    }

    public async Task<RefreshToken?> ValidateRefreshTokenAsync(string rawToken)
    {
        var token = await _repo.GetByTokenHashAsync(HashToken(rawToken));        if (token is null || token.RevokedAt.HasValue || token.ExpiresAt < DateTime.UtcNow)
            return null;
        return token;
    }

    public async Task<RefreshToken?> FindByRawTokenAsync(string rawToken)
        => await _repo.GetByTokenHashAsync(HashToken(rawToken));

    public async Task LinkRotationAsync(Guid oldTokenId, Guid newTokenId)
    {
        var old = await _repo.GetByIdAsync(oldTokenId);
        if (old is not null)
        {
            old.RevokedAt = DateTime.UtcNow;
            old.ReplacedByTokenId = newTokenId;
            await _repo.UpdateAsync(old);
        }
    }

    public async Task RevokeRefreshTokenAsync(Guid tokenId)
    {
        var token = await _repo.GetByIdAsync(tokenId);
        if (token is not null) { token.RevokedAt = DateTime.UtcNow; await _repo.UpdateAsync(token); }
    }

    public async Task RevokeTokenFamilyAsync(Guid tokenId) => await _repo.RevokeTokenFamilyAsync(tokenId);

    public string HashToken(string token)
    {
        using var sha = SHA256.Create();
        return Convert.ToHexString(sha.ComputeHash(Encoding.UTF8.GetBytes(token)));
    }
}
