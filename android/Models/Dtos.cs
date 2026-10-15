namespace NexSync.Android.Models;

public record UserDto(string Id, string Email, string FullName);

public record AuthPayload(string AccessToken, UserDto User);

public record DeviceDto(
    string DeviceId,
    string Name,
    string Platform,
    string Status,
    string CreatedAt,
    string LastSeenAt,
    string? RevokedAt);

public record FolderDto(
    string Id,
    string Name,
    string? ParentFolderId,
    string CreatedAt,
    string UpdatedAt,
    string? OriginDeviceId);

public record FileDto(
    string Id,
    string Name,
    string? FolderId,
    string Hash,
    long Size,
    string ContentType,
    string CreatedAt,
    string UpdatedAt,
    string? OriginDeviceId);

public record FolderContents(
    List<FolderDto> Folders,
    List<FileDto> Files,
    int Page,
    int PageSize,
    int TotalCount,
    int TotalPages);
