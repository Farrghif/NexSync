namespace NexSync.Application.DTOs;

public record ChangeItemDto(
    long Sequence,
    string EntityType,
    Guid EntityId,
    string Operation,
    string? Name,
    Guid? ParentFolderId,
    string? Hash,
    long? Size,
    string? ContentType,
    Guid? OriginDeviceId,
    DateTime OccurredAt);

public record PullResponse(
    List<ChangeItemDto> Changes,
    long NextCursor,
    bool HasMore,
    long HighWaterCursor);

public record CursorResponse(long HighWaterCursor);
