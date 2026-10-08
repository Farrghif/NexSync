namespace NexSync.Application.Interfaces;

public sealed record MutationContext(Guid OperationId, string Fingerprint, int SuccessStatus);

public interface IIdempotencyStore
{
    Task<IdempotencyRecord?> FindAsync(Guid operationId, CancellationToken cancellationToken = default);
    Task StageAsync(Guid operationId, Guid userId, Guid deviceId, string fingerprint, int resultStatus, string resultPayload, Guid? entityId, long? sequence, CancellationToken cancellationToken = default);
}

public sealed record IdempotencyRecord(
    Guid OperationId,
    Guid UserId,
    Guid DeviceId,
    string RequestFingerprint,
    int ResultStatus,
    string? ResultPayload,
    Guid? EntityId,
    long? Sequence);
