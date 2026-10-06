using Microsoft.EntityFrameworkCore;
using NexSync.Application.Interfaces;
using NexSync.Domain.Entities;
using NexSync.Infrastructure.Data;

namespace NexSync.Infrastructure.Repositories;

public class RefreshTokenRepository : Repository<RefreshToken>, IRefreshTokenRepository
{
    public RefreshTokenRepository(AppDbContext context) : base(context) { }

    public async Task<RefreshToken?> GetByTokenHashAsync(string tokenHash)
    {
        return await _context.RefreshTokens
            .FirstOrDefaultAsync(t => t.TokenHash == tokenHash);
    }

    public async Task<RefreshToken?> GetByIdWithUserAsync(Guid id)
    {
        return await _context.RefreshTokens
            .Include(t => t.User)
            .FirstOrDefaultAsync(t => t.Id == id);
    }

    public async Task RevokeTokenFamilyAsync(Guid originalTokenId)
    {
        var token = await _context.RefreshTokens.FindAsync(originalTokenId);
        if (token is null)
            return;

        var toRevoke = new HashSet<Guid>();

        await CollectTokensBackwardAsync(token.Id, toRevoke);
        await CollectTokensForwardAsync(token.Id, toRevoke);

        foreach (var tokenId in toRevoke)
        {
            var t = await _context.RefreshTokens.FindAsync(tokenId);
            if (t is not null && !t.RevokedAt.HasValue)
            {
                t.RevokedAt = DateTime.UtcNow;
            }
        }

        await _context.SaveChangesAsync();
    }

    public async Task<IEnumerable<RefreshToken>> GetActiveTokensByUserAsync(Guid userId)
    {
        return await _context.RefreshTokens
            .Where(t => t.UserId == userId && !t.RevokedAt.HasValue && t.ExpiresAt > DateTime.UtcNow)
            .ToListAsync();
    }

    private async Task CollectTokensBackwardAsync(Guid tokenId, HashSet<Guid> collected)
    {
        var token = await _context.RefreshTokens.FindAsync(tokenId);
        if (token is null || collected.Contains(tokenId))
            return;

        collected.Add(tokenId);

        var previous = await _context.RefreshTokens
            .FirstOrDefaultAsync(t => t.ReplacedByTokenId == tokenId);

        if (previous is not null)
        {
            await CollectTokensBackwardAsync(previous.Id, collected);
        }
    }

    private async Task CollectTokensForwardAsync(Guid tokenId, HashSet<Guid> collected)
    {
        var token = await _context.RefreshTokens.FindAsync(tokenId);
        if (token is null || collected.Contains(tokenId))
            return;

        collected.Add(tokenId);

        if (token.ReplacedByTokenId.HasValue)
        {
            await CollectTokensForwardAsync(token.ReplacedByTokenId.Value, collected);
        }
    }
}
