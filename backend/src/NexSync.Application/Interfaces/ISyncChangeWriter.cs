using NexSync.Domain.Entities;

namespace NexSync.Application.Interfaces;

public interface ISyncChangeWriter
{
    Task<ChangeLog> WriteFolderChangeAsync(
        Guid userId,
        Guid folderId,
        SyncOperation operation,
        string name,
        Guid? parentFolderId,
        Guid? originDeviceId = null,
        CancellationToken cancellationToken = default);

    Task<ChangeLog> WriteFileChangeAsync(
        Guid userId,
        Guid fileId,
        SyncOperation operation,
        string name,
        Guid? parentFolderId,
        string? hash = null,
        long? size = null,
        string? contentType = null,
        Guid? originDeviceId = null,
        CancellationToken cancellationToken = default);
}
