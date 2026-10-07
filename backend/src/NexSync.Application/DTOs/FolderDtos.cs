namespace NexSync.Application.DTOs;

public record CreateFolderRequest(string Name, Guid? ParentFolderId);
public record UpdateFolderRequest(string Name, Guid? ParentFolderId);
public record FolderDto(Guid Id, string Name, Guid? ParentFolderId, DateTime CreatedAt, DateTime UpdatedAt);
public record FolderContentsResponse(List<FolderDto> Folders, List<FileDto> Files, int Page, int PageSize, int TotalCount, int TotalPages);
