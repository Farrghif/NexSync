using NexSync.Application.DTOs;
using NexSync.Application.Interfaces;
using NexSync.Application.Validators;
using NexSync.Domain.Entities;
using NexSync.Domain.Exceptions;

namespace NexSync.Application.Services;

public class FileService(
    IFileRepository files,
    IFolderRepository folders,
    IStorageService storage,
    ITransactionProvider transactions,
    ISyncChangeWriter changes) : IFileService
{
    private static FileDto Map(FileEntry f) => new(f.Id, f.Name, f.FolderId, f.Hash, f.Size, f.ContentType, f.CreatedAt, f.UpdatedAt);

    public async Task<FileDto> UploadAsync(Guid userId, Stream fileStream, string fileName, string contentType, Guid? folderId, Guid? deviceId = null)
    {
        FileNameValidator.Validate(fileName);
        if (folderId.HasValue)
        {
            var folder = await folders.GetByIdAsync(folderId.Value);
            if (folder is null || folder.OwnerId != userId)
                throw new ForbiddenException("Access denied.");
        }
        if (await files.NameExistsInFolderAsync(userId, folderId, fileName))
            throw new ConflictException("File name already exists.");
        var (storagePath, hash, size) = await storage.SaveFileAsync(userId, fileStream, fileName);
        var entry = new FileEntry
        {
            Id = Guid.NewGuid(), Name = fileName, FolderId = folderId, OwnerId = userId,
            Hash = hash, Size = size, ContentType = contentType, StoragePath = storagePath,
            CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow
        };
        await using var tx = await transactions.BeginTransactionAsync();
        try
        {
            await files.StageAsync(entry);
            await changes.WriteFileChangeAsync(userId, entry.Id, SyncOperation.Created, fileName, folderId, hash, size, contentType, deviceId);
            await files.SaveChangesAsync();
            await tx.CommitAsync();
        }
        catch
        {
            await tx.RollbackAsync();
            var refs = await files.GetCountByStoragePathAsync(storagePath);
            if (refs == 0) await storage.DeleteFileAsync(storagePath);
            throw;
        }
        return Map(entry);
    }

    public async Task<FileDto> GetByIdAsync(Guid userId, Guid fileId)
    {
        var f = await files.GetByIdAsync(fileId);
        if (f is null || f.OwnerId != userId)
            throw new ForbiddenException("Access denied.");
        return Map(f);
    }

    public async Task<Stream> DownloadAsync(Guid userId, Guid fileId)
    {
        var f = await files.GetByIdAsync(fileId);
        if (f is null || f.OwnerId != userId)
            throw new ForbiddenException("Access denied.");
        return await storage.GetFileAsync(f.StoragePath);
    }

    public async Task<FileDto> UpdateAsync(Guid userId, Guid fileId, string name, Guid? folderId, Guid? deviceId = null)
    {
        FileNameValidator.Validate(name);
        var f = await files.GetByIdAsync(fileId);
        if (f is null || f.OwnerId != userId)
            throw new ForbiddenException("Access denied.");
        if (folderId.HasValue)
        {
            var folder = await folders.GetByIdAsync(folderId.Value);
            if (folder is null || folder.OwnerId != userId)
                throw new ForbiddenException("Access denied.");
        }
        var nameChanged = !string.Equals(f.Name, name, StringComparison.OrdinalIgnoreCase);
        var parentChanged = f.FolderId != folderId;
        var moved = nameChanged || parentChanged;
        if (moved && await files.NameExistsInFolderAsync(userId, folderId, name))
        {
            var conflict = (await files.GetByFolderAsync(userId, folderId, 1, int.MaxValue))
                .Any(x => x.Id != fileId && string.Equals(x.Name, name, StringComparison.OrdinalIgnoreCase));
            if (conflict) throw new ConflictException("File name already exists in target location.");
        }
        var op = parentChanged ? SyncOperation.Moved
            : nameChanged ? SyncOperation.Renamed
            : SyncOperation.Modified;
        f.Name = name;
        f.FolderId = folderId;
        f.UpdatedAt = DateTime.UtcNow;
        await using var tx = await transactions.BeginTransactionAsync();
        try
        {
            files.StageUpdate(f);
            await changes.WriteFileChangeAsync(userId, fileId, op, name, folderId, f.Hash, f.Size, f.ContentType, deviceId);
            await files.SaveChangesAsync();
            await tx.CommitAsync();
        }
        catch
        {
            await tx.RollbackAsync();
            throw;
        }
        return Map(f);
    }

    public async Task DeleteAsync(Guid userId, Guid fileId, Guid? deviceId = null)
    {
        var f = await files.GetByIdAsync(fileId);
        if (f is null || f.OwnerId != userId)
            throw new ForbiddenException("Access denied.");
        var tombstone = (f.Id, f.Name, f.FolderId, f.StoragePath);
        await using var tx = await transactions.BeginTransactionAsync();
        try
        {
            files.StageDelete(f);
            await changes.WriteFileChangeAsync(userId, tombstone.Id, SyncOperation.Deleted, tombstone.Name, tombstone.FolderId, originDeviceId: deviceId);
            await files.SaveChangesAsync();
            await tx.CommitAsync();
        }
        catch
        {
            await tx.RollbackAsync();
            throw;
        }
        var refs = await files.GetCountByStoragePathAsync(tombstone.StoragePath);
        if (refs == 0) await storage.DeleteFileAsync(tombstone.StoragePath);
    }
}
