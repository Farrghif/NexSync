using System.Text.Json;
using NexSync.Application.DTOs;
using NexSync.Application.Interfaces;
using NexSync.Application.Validators;
using NexSync.Domain.Entities;
using NexSync.Domain.Exceptions;
using NexSync.Domain.Sync;

namespace NexSync.Application.Services;

public class FolderService(
    IFolderRepository folders,
    IFileRepository files,
    IStorageService storage,
    ITransactionProvider transactions,
    ISyncChangeWriter changes,
    IIdempotencyStore idempotency) : IFolderService
{
    private static readonly JsonSerializerOptions PayloadJson = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    private static T ReplayOrThrow<T>(Guid userId, IdempotencyRecord record, string fingerprint)
    {
        if (record.UserId != userId || !string.Equals(record.RequestFingerprint, fingerprint, StringComparison.Ordinal))
            throw new OperationIdReuseException("X-Operation-Id was already used for a different request.");
        return JsonSerializer.Deserialize<T>(record.ResultPayload!, PayloadJson)
            ?? throw new InvalidOperationException("Stored idempotency payload is corrupted.");
    }

    private static void EnsureOwned(IdempotencyRecord record, Guid userId, string fingerprint)
    {
        if (record.UserId != userId || !string.Equals(record.RequestFingerprint, fingerprint, StringComparison.Ordinal))
            throw new OperationIdReuseException("X-Operation-Id was already used for a different request.");
    }

    private static FolderDto Map(Folder f) => new(f.Id, f.Name, f.ParentFolderId, f.CreatedAt, f.UpdatedAt);
    private static FileDto MapFile(FileEntry f) => new(f.Id, f.Name, f.FolderId, f.Hash, f.Size, f.ContentType, f.CreatedAt, f.UpdatedAt);

    public async Task<FolderContentsResponse> GetContentsAsync(Guid userId, Guid? parentId, int page, int pageSize)
    {
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 100);
        if (parentId.HasValue)
        {
            var parent = await folders.GetByIdAsync(parentId.Value);
            if (parent is null || parent.OwnerId != userId)
                throw new ForbiddenException("Access denied.");
        }
        var folderList = (await folders.GetByParentAsync(userId, parentId, 1, int.MaxValue))
            .OrderBy(f => f.Name, StringComparer.OrdinalIgnoreCase).ToList();
        var fileList = (await files.GetByFolderAsync(userId, parentId, 1, int.MaxValue))
            .OrderBy(f => f.Name, StringComparer.OrdinalIgnoreCase).ToList();
        var total = folderList.Count + fileList.Count;
        var pages = Math.Max(1, (int)Math.Ceiling((double)total / pageSize));
        var combined = folderList.Cast<object>().Concat(fileList.Cast<object>()).ToList();
        var slice = combined.Skip((page - 1) * pageSize).Take(pageSize).ToList();
        return new FolderContentsResponse(
            slice.OfType<Folder>().Select(Map).ToList(),
            slice.OfType<FileEntry>().Select(MapFile).ToList(),
            page, pageSize, total, pages);
    }

    public async Task<FolderDto> GetByIdAsync(Guid userId, Guid folderId)
    {
        var f = await folders.GetByIdAsync(folderId);
        if (f is null || f.OwnerId != userId)
            throw new ForbiddenException("Access denied.");
        return Map(f);
    }

    public async Task<FolderDto> CreateAsync(Guid userId, string name, Guid? parentFolderId, Guid? deviceId = null, MutationContext? mutation = null)
    {
        FileNameValidator.Validate(name);
        if (parentFolderId.HasValue)
        {
            var parent = await folders.GetByIdAsync(parentFolderId.Value);
            if (parent is null || parent.OwnerId != userId)
                throw new ForbiddenException("Access denied.");
        }
        if (await folders.NameExistsInParentAsync(userId, parentFolderId, name))
            throw new ConflictException("Folder name already exists.");
        var fingerprint = mutation is null ? null : RequestFingerprint.ForMutation("POST", "/api/folders",
            ("name", name), ("parentFolderId", parentFolderId?.ToString()));
        if (mutation is not null)
        {
            var seen = await idempotency.FindAsync(mutation.OperationId);
            if (seen is not null) return ReplayOrThrow<FolderDto>(userId, seen, fingerprint!);
        }
        var folder = new Folder
        {
            Id = Guid.NewGuid(), Name = name, ParentFolderId = parentFolderId,
            OwnerId = userId, CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow
        };
        await using var tx = await transactions.BeginTransactionAsync();
        try
        {
            await folders.StageAsync(folder);
            var change = await changes.WriteFolderChangeAsync(userId, folder.Id, SyncOperation.Created, name, parentFolderId, deviceId);
            var dto = Map(folder);
            if (mutation is not null)
                await idempotency.StageAsync(mutation.OperationId, userId, deviceId ?? Guid.Empty, fingerprint!,
                    mutation.SuccessStatus, JsonSerializer.Serialize(dto, PayloadJson), folder.Id, change.Sequence);
            await folders.SaveChangesAsync();
            await tx.CommitAsync();
            return dto;
        }
        catch (Exception) when (mutation is not null)
        {
            await tx.RollbackAsync();
            var raced = await idempotency.FindAsync(mutation.OperationId);
            if (raced is not null) return ReplayOrThrow<FolderDto>(userId, raced, fingerprint!);
            throw;
        }
        catch
        {
            await tx.RollbackAsync();
            throw;
        }
    }

    public async Task<FolderDto> UpdateAsync(Guid userId, Guid folderId, string name, Guid? parentFolderId, Guid? deviceId = null, MutationContext? mutation = null)
    {
        FileNameValidator.Validate(name);
        var folder = await folders.GetByIdAsync(folderId);
        if (folder is null || folder.OwnerId != userId)
            throw new ForbiddenException("Access denied.");
        var fingerprint = mutation is null ? null : RequestFingerprint.ForMutation("PUT", $"/api/folders/{folderId}",
            ("name", name), ("parentFolderId", parentFolderId?.ToString()));
        if (mutation is not null)
        {
            var seen = await idempotency.FindAsync(mutation.OperationId);
            if (seen is not null) return ReplayOrThrow<FolderDto>(userId, seen, fingerprint!);
        }
        if (folderId == parentFolderId)
            throw new ConflictException("Cannot move folder into itself.");
        if (parentFolderId.HasValue)
        {
            var target = await folders.GetByIdAsync(parentFolderId.Value);
            if (target is null || target.OwnerId != userId)
                throw new ForbiddenException("Access denied.");
            var descendants = await folders.GetDescendantsAsync(folderId);
            if (descendants.Any(d => d.Id == parentFolderId.Value))
                throw new ConflictException("Cannot move folder into one of its descendants.");
        }
        var nameChanged = !string.Equals(folder.Name, name, StringComparison.OrdinalIgnoreCase);
        var parentChanged = folder.ParentFolderId != parentFolderId;
        if ((nameChanged || parentChanged) && await folders.NameExistsInParentAsync(userId, parentFolderId, name))
        {
            var conflict = (await folders.GetByParentAsync(userId, parentFolderId, 1, int.MaxValue))
                .Any(f => f.Id != folderId && string.Equals(f.Name, name, StringComparison.OrdinalIgnoreCase));
            if (conflict) throw new ConflictException("Folder name already exists in target location.");
        }
        var op = parentChanged ? SyncOperation.Moved
            : nameChanged ? SyncOperation.Renamed
            : SyncOperation.Modified;
        folder.Name = name;
        folder.ParentFolderId = parentFolderId;
        folder.UpdatedAt = DateTime.UtcNow;
        await using var tx = await transactions.BeginTransactionAsync();
        try
        {
            folders.StageUpdate(folder);
            var change = await changes.WriteFolderChangeAsync(userId, folderId, op, name, parentFolderId, deviceId);
            var dto = Map(folder);
            if (mutation is not null)
                await idempotency.StageAsync(mutation.OperationId, userId, deviceId ?? Guid.Empty, fingerprint!,
                    mutation.SuccessStatus, JsonSerializer.Serialize(dto, PayloadJson), folderId, change.Sequence);
            await folders.SaveChangesAsync();
            await tx.CommitAsync();
            return dto;
        }
        catch (Exception) when (mutation is not null)
        {
            await tx.RollbackAsync();
            var raced = await idempotency.FindAsync(mutation.OperationId);
            if (raced is not null) return ReplayOrThrow<FolderDto>(userId, raced, fingerprint!);
            throw;
        }
        catch
        {
            await tx.RollbackAsync();
            throw;
        }
    }

    public async Task DeleteAsync(Guid userId, Guid folderId, Guid? deviceId = null, MutationContext? mutation = null)
    {
        var folder = await folders.GetByIdAsync(folderId);
        if (folder is null || folder.OwnerId != userId)
            throw new ForbiddenException("Access denied.");
        var fingerprint = mutation is null ? null : RequestFingerprint.ForMutation("DELETE", $"/api/folders/{folderId}");
        if (mutation is not null)
        {
            var seen = await idempotency.FindAsync(mutation.OperationId);
            if (seen is not null)
            {
                EnsureOwned(seen, userId, fingerprint!);
                return;
            }
        }
        var descendants = (await folders.GetDescendantsAsync(folderId)).ToList();
        var allIds = new[] { folderId }.Concat(descendants.Select(d => d.Id)).ToList();
        var byId = new Dictionary<Guid, Folder> { [folderId] = folder };
        foreach (var d in descendants) byId[d.Id] = d;
        var fileTombstones = new List<(Guid Id, string Name, Guid? ParentId, string StoragePath)>();
        foreach (var fid in allIds)
        {
            var fileList = await files.GetByFolderAsync(userId, fid, 1, int.MaxValue);
            foreach (var file in fileList)
                fileTombstones.Add((file.Id, file.Name, file.FolderId, file.StoragePath));
        }
        await using var tx = await transactions.BeginTransactionAsync();
        try
        {
            long? rootSequence = null;
            foreach (var (id, name, parentId, _) in fileTombstones)
            {
                var f = await files.GetByIdAsync(id);
                if (f is not null) files.StageDelete(f);
                await changes.WriteFileChangeAsync(userId, id, SyncOperation.Deleted, name, parentId, originDeviceId: deviceId);
            }
            foreach (var fid in allIds.AsEnumerable().Reverse())
            {
                var f = byId[fid];
                folders.StageDelete(f);
                var change = await changes.WriteFolderChangeAsync(userId, fid, SyncOperation.Deleted, f.Name, f.ParentFolderId, deviceId);
                if (fid == folderId) rootSequence = change.Sequence;
            }
            if (mutation is not null)
                await idempotency.StageAsync(mutation.OperationId, userId, deviceId ?? Guid.Empty, fingerprint!,
                    mutation.SuccessStatus, string.Empty, folderId, rootSequence);
            await folders.SaveChangesAsync();
            await tx.CommitAsync();
        }
        catch (Exception) when (mutation is not null)
        {
            await tx.RollbackAsync();
            var raced = await idempotency.FindAsync(mutation.OperationId);
            if (raced is not null)
            {
                EnsureOwned(raced, userId, fingerprint!);
                return;
            }
            throw;
        }
        catch
        {
            await tx.RollbackAsync();
            throw;
        }
        foreach (var (_, _, _, storagePath) in fileTombstones)
        {
            var refs = await files.GetCountByStoragePathAsync(storagePath);
            if (refs == 0) await storage.DeleteFileAsync(storagePath);
        }
    }
}

