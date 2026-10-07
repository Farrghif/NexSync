namespace NexSync.Domain.Entities;

public class ProcessedOperation
{
    public Guid OperationId { get; set; }
    public Guid UserId { get; set; }
    public Guid DeviceId { get; set; }
    public string RequestFingerprint { get; set; } = null!;
    public int ResultStatus { get; set; }
    public string? ResultPayload { get; set; }
    public Guid? EntityId { get; set; }
    public long? Sequence { get; set; }
    public DateTime CreatedAt { get; set; }
}
