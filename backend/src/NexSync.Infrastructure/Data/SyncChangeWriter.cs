using NexSync.Application.Interfaces;
using NexSync.Domain.Entities;
using NexSync.Infrastructure.Data;

namespace NexSync.Infrastructure.Data;

public class SyncChangeWriter(AppDbContext context, ISyncSequenceAllocator allocator) : ISyncChangeWriter
{
    public async Task<ChangeLog> WriteFolderChangeAsync(
        Guid userId,
        Guid folderId,
        SyncOperation operation,
        string name,
        Guid? parentFolderId,
        Guid? originDeviceId = null,
        CancellationToken cancellationToken = default)
    {
        var seq = await allocator.AllocateAsync(userId, cancellationToken);
        var entry = new ChangeLog
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            Sequence = seq,
            EntityType = SyncEntityType.Folder,
            EntityId = folderId,
            Operation = operation,
            Name = name,
            ParentFolderId = parentFolderId,
            Hash = null,
            Size = null,
            ContentType = null,
            OriginDeviceId = originDeviceId,
            OccurredAt = DateTime.UtcNow
        };
        context.ChangeLogs.Add(entry);
        await context.SaveChangesAsync(cancellationToken);
        return entry;
    }

    public async Task<ChangeLog> WriteFileChangeAsync(
        Guid userId,
        Guid fileId,
        SyncOperation operation,
        string name,
        Guid? parentFolderId,
        string? hash = null,
        long? size = null,
        string? contentType = null,
        Guid? originDeviceId = null,
        CancellationToken cancellationToken = default)
    {
        var seq = await allocator.AllocateAsync(userId, cancellationToken);
        var entry = new ChangeLog
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            Sequence = seq,
            EntityType = SyncEntityType.File,
            EntityId = fileId,
            Operation = operation,
            Name = name,
            ParentFolderId = parentFolderId,
            Hash = hash,
            Size = size,
            ContentType = contentType,
            OriginDeviceId = originDeviceId,
            OccurredAt = DateTime.UtcNow
        };
        context.ChangeLogs.Add(entry);
        await context.SaveChangesAsync(cancellationToken);
        return entry;
    }
}
