namespace NexSync.Domain.Entities;

public class FileEntry
{
    public Guid Id { get; set; }
    public string Name { get; set; } = null!;
    public Guid? FolderId { get; set; }
    public Guid OwnerId { get; set; }
    public string Hash { get; set; } = null!;
    public long Size { get; set; }
    public string ContentType { get; set; } = null!;
    public string StoragePath { get; set; } = null!;
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    public Folder? Folder { get; set; }
    public User Owner { get; set; } = null!;
}
