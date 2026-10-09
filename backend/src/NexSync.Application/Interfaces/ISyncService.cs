using NexSync.Application.DTOs;

namespace NexSync.Application.Interfaces;

public interface ISyncService
{
    Task<PullResponse> PullAsync(Guid userId, long since, long until, int limit, CancellationToken cancellationToken = default);
    Task<CursorResponse> GetCursorAsync(Guid userId, CancellationToken cancellationToken = default);
}
