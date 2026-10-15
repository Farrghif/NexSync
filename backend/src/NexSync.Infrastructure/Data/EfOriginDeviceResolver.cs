using Microsoft.EntityFrameworkCore;
using NexSync.Application.Interfaces;

namespace NexSync.Infrastructure.Data;

public sealed class EfOriginDeviceResolver(AppDbContext context) : IOriginDeviceResolver
{
    public async Task<Guid?> GetOriginDeviceIdAsync(Guid entityId)
        => await context.ChangeLogs
            .Where(c => c.EntityId == entityId)
            .OrderByDescending(c => c.Sequence)
            .Select(c => c.OriginDeviceId)
            .FirstOrDefaultAsync();
}
