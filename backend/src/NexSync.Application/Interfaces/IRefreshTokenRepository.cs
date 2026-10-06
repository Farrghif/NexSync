using NexSync.Domain.Entities;

namespace NexSync.Application.Interfaces;

public interface IRefreshTokenRepository : IRepository<RefreshToken>
{
    Task<RefreshToken?> GetByTokenHashAsync(string tokenHash);
    Task<RefreshToken?> GetByIdWithUserAsync(Guid id);
    Task RevokeTokenFamilyAsync(Guid originalTokenId);
    Task<IEnumerable<RefreshToken>> GetActiveTokensByUserAsync(Guid userId);
}
