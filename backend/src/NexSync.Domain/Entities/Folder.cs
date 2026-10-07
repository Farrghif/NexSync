namespace NexSync.Domain.Entities;

public class Folder
{
    public Guid Id { get; set; }
    public string Name { get; set; } = null!;
    public Guid? ParentFolderId { get; set; }
    public Guid OwnerId { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    public Folder? ParentFolder { get; set; }
    public ICollection<Folder> Children { get; set; } = new List<Folder>();
    public User Owner { get; set; } = null!;
    public ICollection<FileEntry> Files { get; set; } = new List<FileEntry>();
}
