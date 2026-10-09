using Microsoft.EntityFrameworkCore;
using NexSync.Application.DTOs;
using NexSync.Application.Interfaces;
using NexSync.Domain.Exceptions;

namespace NexSync.Infrastructure.Data;

public class SyncService(AppDbContext context) : ISyncService
{
    public const int DefaultLimit = 500;
    public const int MaxLimit = 500;

    public async Task<PullResponse> PullAsync(Guid userId, long since, long until, int limit, CancellationToken cancellationToken = default)
    {
        if (since < 0)
            throw new DomainException("Query parameter 'since' must be >= 0.");
        if (until < since)
            throw new DomainException("Query parameter 'until' must be >= 'since'.");
        limit = Math.Clamp(limit <= 0 ? DefaultLimit : limit, 1, MaxLimit);

        var rows = await context.ChangeLogs
            .AsNoTracking()
            .Where(c => c.UserId == userId && c.Sequence > since && c.Sequence <= until)
            .OrderBy(c => c.Sequence)
            .Take(limit + 1)
            .Select(c => new ChangeItemDto(
                c.Sequence,
                c.EntityType.ToString(),
                c.EntityId,
                c.Operation.ToString(),
                c.Name,
                c.ParentFolderId,
                c.Hash,
                c.Size,
                c.ContentType,
                c.OriginDeviceId,
                c.OccurredAt))
            .ToListAsync(cancellationToken);

        var hasMore = rows.Count > limit;
        var changes = hasMore ? rows.Take(limit).ToList() : rows;
        var nextCursor = changes.Count == 0 ? since : changes[^1].Sequence;
        return new PullResponse(changes, nextCursor, hasMore, until);
    }

    public async Task<CursorResponse> GetCursorAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        var max = await context.ChangeLogs
            .AsNoTracking()
            .Where(c => c.UserId == userId)
            .MaxAsync(c => (long?)c.Sequence, cancellationToken);
        return new CursorResponse(max ?? 0);
    }
}
