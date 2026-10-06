namespace NexSync.Domain.Entities;

public class User
{
    public Guid Id { get; set; }
    public string Email { get; set; } = null!;
    public string PasswordHash { get; set; } = null!;
    public string FullName { get; set; } = null!;
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    
    public ICollection<Folder> Folders { get; set; } = [];
    public ICollection<File> Files { get; set; } = [];
    public ICollection<RefreshToken> RefreshTokens { get; set; } = [];
}
