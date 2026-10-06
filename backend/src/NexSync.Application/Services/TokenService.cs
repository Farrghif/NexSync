using System.Text;
using NexSync.Application.Interfaces;
using NexSync.Domain.Entities;

namespace NexSync.Application.Services;

public class TokenService : ITokenService
{
    private readonly IRefreshTokenRepository _tokenRepository;
    
    public TokenService(IRefreshTokenRepository tokenRepository)
    {
        _tokenRepository = tokenRepository;
    }
    
    public async Task<RefreshToken> CreateRefreshTokenAsync(Guid userId)
    {
        var token = new RefreshToken
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            TokenHash = HashToken(Guid.NewGuid().ToString()),
            ExpiresAt = DateTime.UtcNow.AddDays(7),
            CreatedAt = DateTime.UtcNow
        };
        
        await _tokenRepository.AddAsync(token);
        return token;
    }
    
    public async Task<RefreshToken?> ValidateRefreshTokenAsync(string tokenHash)
    {
        var token = await _tokenRepository.GetByTokenHashAsync(tokenHash);
        
        if (token is null || token.RevokedAt.HasValue || token.ExpiresAt < DateTime.UtcNow)
            return null;
        
        return token;
    }
    
    public async Task RevokeRefreshTokenAsync(Guid tokenId)
    {
        var token = await _tokenRepository.GetByIdAsync(tokenId);
        if (token is not null)
        {
            token.RevokedAt = DateTime.UtcNow;
            await _tokenRepository.UpdateAsync(token);
        }
    }
    
    public async Task RevokeTokenFamilyAsync(Guid tokenId)
    {
        await _tokenRepository.RevokeTokenFamilyAsync(tokenId);
    }
    
    public string HashToken(string token)
    {
        using var sha256 = System.Security.Cryptography.SHA256.Create();
        return Convert.ToHexString(sha256.ComputeHash(Encoding.UTF8.GetBytes(token)));
    }
}
