using System.Text.Json;
using NexSync.Application.DTOs;
using NexSync.Application.Interfaces;
using NexSync.Application.Validators;
using NexSync.Domain.Entities;
using NexSync.Domain.Exceptions;
using NexSync.Domain.Sync;

namespace NexSync.Application.Services;

public class FileService(
    IFileRepository files,
    IFolderRepository folders,
    IStorageService storage,
    ITransactionProvider transactions,
    ISyncChangeWriter changes,
    IIdempotencyStore idempotency) : IFileService
{
    private static readonly JsonSerializerOptions PayloadJson = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
    private static FileDto Map(FileEntry f) => new(f.Id, f.Name, f.FolderId, f.Hash, f.Size, f.ContentType, f.CreatedAt, f.UpdatedAt);

    public async Task<FileDto> UploadAsync(Guid userId, Stream fileStream, string fileName, string contentType, Guid? folderId, Guid? deviceId = null, MutationContext? mutation = null)
    {
        FileNameValidator.Validate(fileName);
        if (folderId.HasValue)
        {
            var folder = await folders.GetByIdAsync(folderId.Value);
            if (folder is null || folder.OwnerId != userId)
                throw new ForbiddenException("Access denied.");
        }
        var (storagePath, hash, size) = await storage.SaveFileAsync(userId, fileStream, fileName);
        string? fingerprint = mutation is null ? null : RequestFingerprint.ForMutation("POST", "/api/files/upload",
            ("name", fileName), ("folderId", folderId?.ToString()), ("hash", hash),
            ("size", size.ToString()), ("contentType", contentType));
        if (mutation is not null)
        {
            var seen = await idempotency.FindAsync(mutation.OperationId);
            if (seen is not null)
            {
                await DiscardTempIfOrphanAsync(storagePath);
                return ReplayOrThrow<FileDto>(userId, seen, fingerprint!);
            }
        }
        if (await files.NameExistsInFolderAsync(userId, folderId, fileName))
        {
            await DiscardTempIfOrphanAsync(storagePath);
            throw new ConflictException("File name already exists.");
        }
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
            var change = await changes.WriteFileChangeAsync(userId, entry.Id, SyncOperation.Created, fileName, folderId, hash, size, contentType, deviceId);
            var dto = Map(entry);
            if (mutation is not null)
                await idempotency.StageAsync(mutation.OperationId, userId, deviceId ?? Guid.Empty, fingerprint!,
                    mutation.SuccessStatus, JsonSerializer.Serialize(dto, PayloadJson), entry.Id, change.Sequence);
            await files.SaveChangesAsync();
            await tx.CommitAsync();
            return dto;
        }
        catch (Exception) when (mutation is not null)
        {
            await tx.RollbackAsync();
            await DiscardTempIfOrphanAsync(storagePath);
            var raced = await idempotency.FindAsync(mutation.OperationId);
            if (raced is not null && raced.UserId == userId
                && string.Equals(raced.RequestFingerprint, fingerprint, StringComparison.Ordinal))
                return ReplayOrThrow<FileDto>(userId, raced, fingerprint!);
            throw;
        }
        catch
        {
            await tx.RollbackAsync();
            var refs = await files.GetCountByStoragePathAsync(storagePath);
            if (refs == 0) await storage.DeleteFileAsync(storagePath);
            throw;
        }
    }

    private async Task DiscardTempIfOrphanAsync(string storagePath)
    {
        var refs = await files.GetCountByStoragePathAsync(storagePath);
        if (refs == 0) await storage.DeleteFileAsync(storagePath);
    }

    private static T ReplayOrThrow<T>(Guid userId, IdempotencyRecord record, string fingerprint)
    {
        if (record.UserId != userId || !string.Equals(record.RequestFingerprint, fingerprint, StringComparison.Ordinal))
            throw new OperationIdReuseException("X-Operation-Id was already used for a different request.");
        return JsonSerializer.Deserialize<T>(record.ResultPayload!, PayloadJson)
            ?? throw new InvalidOperationException("Stored idempotency payload is corrupted.");
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

    public async Task<FileDto> UpdateAsync(Guid userId, Guid fileId, string name, Guid? folderId, Guid? deviceId = null, MutationContext? mutation = null)
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
        var fingerprint = mutation is null ? null : RequestFingerprint.ForMutation("PUT", $"/api/files/{fileId}",
            ("name", name), ("folderId", folderId?.ToString()));
        if (mutation is not null)
        {
            var seen = await idempotency.FindAsync(mutation.OperationId);
            if (seen is not null) return ReplayOrThrow<FileDto>(userId, seen, fingerprint!);
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
            var change = await changes.WriteFileChangeAsync(userId, fileId, op, name, folderId, f.Hash, f.Size, f.ContentType, deviceId);
            var dto = Map(f);
            if (mutation is not null)
                await idempotency.StageAsync(mutation.OperationId, userId, deviceId ?? Guid.Empty, fingerprint!,
                    mutation.SuccessStatus, JsonSerializer.Serialize(dto, PayloadJson), fileId, change.Sequence);
            await files.SaveChangesAsync();
            await tx.CommitAsync();
            return dto;
        }
        catch (Exception) when (mutation is not null)
        {
            await tx.RollbackAsync();
            var raced = await idempotency.FindAsync(mutation.OperationId);
            if (raced is not null) return ReplayOrThrow<FileDto>(userId, raced, fingerprint!);
            throw;
        }
        catch
        {
            await tx.RollbackAsync();
            throw;
        }
    }

    public async Task DeleteAsync(Guid userId, Guid fileId, Guid? deviceId = null, MutationContext? mutation = null)
    {
        var f = await files.GetByIdAsync(fileId);
        if (f is null || f.OwnerId != userId)
            throw new ForbiddenException("Access denied.");
        var fingerprint = mutation is null ? null : RequestFingerprint.ForMutation("DELETE", $"/api/files/{fileId}");
        if (mutation is not null)
        {
            var seen = await idempotency.FindAsync(mutation.OperationId);
            if (seen is not null)
            {
                if (seen.UserId != userId || !string.Equals(seen.RequestFingerprint, fingerprint, StringComparison.Ordinal))
                    throw new OperationIdReuseException("X-Operation-Id was already used for a different request.");
                return;
            }
        }
        var tombstone = (f.Id, f.Name, f.FolderId, f.StoragePath);
        await using var tx = await transactions.BeginTransactionAsync();
        try
        {
            files.StageDelete(f);
            var change = await changes.WriteFileChangeAsync(userId, tombstone.Id, SyncOperation.Deleted, tombstone.Name, tombstone.FolderId, originDeviceId: deviceId);
            if (mutation is not null)
                await idempotency.StageAsync(mutation.OperationId, userId, deviceId ?? Guid.Empty, fingerprint!,
                    mutation.SuccessStatus, string.Empty, fileId, change.Sequence);
            await files.SaveChangesAsync();
            await tx.CommitAsync();
        }
        catch (Exception) when (mutation is not null)
        {
            await tx.RollbackAsync();
            var raced = await idempotency.FindAsync(mutation.OperationId);
            if (raced is not null)
            {
                if (raced.UserId != userId || !string.Equals(raced.RequestFingerprint, fingerprint, StringComparison.Ordinal))
                    throw new OperationIdReuseException("X-Operation-Id was already used for a different request.");
                return;
            }
            throw;
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

