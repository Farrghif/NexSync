namespace NexSync.Domain.Entities;

public class SyncCounter
{
    public Guid UserId { get; set; }
    public long NextSequence { get; set; }
    public User User { get; set; } = null!;
}
