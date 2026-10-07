namespace NexSync.Domain.Entities;

public class Device
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public string Name { get; set; } = null!;
    public DevicePlatform Platform { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime LastSeenAt { get; set; }
    public DateTime? RevokedAt { get; set; }
    public User User { get; set; } = null!;
}
