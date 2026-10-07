namespace NexSync.Domain.Entities;

public class ChangeLog
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public long Sequence { get; set; }
    public SyncEntityType EntityType { get; set; }
    public Guid EntityId { get; set; }
    public SyncOperation Operation { get; set; }
    public string? Name { get; set; }
    public Guid? ParentFolderId { get; set; }
    public string? Hash { get; set; }
    public long? Size { get; set; }
    public string? ContentType { get; set; }
    public Guid? OriginDeviceId { get; set; }
    public DateTime OccurredAt { get; set; }
    public User User { get; set; } = null!;
}
