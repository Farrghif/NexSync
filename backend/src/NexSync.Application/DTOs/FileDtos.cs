namespace NexSync.Application.DTOs;

public record FileDto(Guid Id, string Name, Guid? FolderId, string Hash, long Size, string ContentType, DateTime CreatedAt, DateTime UpdatedAt);
public record UpdateFileRequest(string Name, Guid? FolderId);
