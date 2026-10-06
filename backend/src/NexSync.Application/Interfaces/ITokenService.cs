using NexSync.Domain.Entities;

namespace NexSync.Application.Interfaces;

public interface ITokenService
{
    Task<RefreshToken> CreateRefreshTokenAsync(Guid userId);
    Task<RefreshToken?> ValidateRefreshTokenAsync(string tokenHash);
    Task RevokeRefreshTokenAsync(Guid tokenId);
    Task RevokeTokenFamilyAsync(Guid tokenId);
    string HashToken(string token);
}
