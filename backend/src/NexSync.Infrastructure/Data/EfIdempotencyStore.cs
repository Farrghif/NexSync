using NexSync.Application.Interfaces;
using NexSync.Domain.Entities;

namespace NexSync.Infrastructure.Data;

public class EfIdempotencyStore(AppDbContext context) : IIdempotencyStore
{
    public async Task<IdempotencyRecord?> FindAsync(Guid operationId, CancellationToken cancellationToken = default)
    {
        var row = await context.ProcessedOperations.FindAsync([operationId], cancellationToken);
        return row is null
            ? null
            : new IdempotencyRecord(row.OperationId, row.UserId, row.DeviceId, row.RequestFingerprint,
                row.ResultStatus, row.ResultPayload, row.EntityId, row.Sequence);
    }

    public Task StageAsync(Guid operationId, Guid userId, Guid deviceId, string fingerprint, int resultStatus, string resultPayload, Guid? entityId, long? sequence, CancellationToken cancellationToken = default)
    {
        context.ProcessedOperations.Add(new ProcessedOperation
        {
            OperationId = operationId,
            UserId = userId,
            DeviceId = deviceId,
            RequestFingerprint = fingerprint,
            ResultStatus = resultStatus,
            ResultPayload = resultPayload,
            EntityId = entityId,
            Sequence = sequence,
            CreatedAt = DateTime.UtcNow
        });
        return Task.CompletedTask;
    }
}
