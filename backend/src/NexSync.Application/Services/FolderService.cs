using NexSync.Application.DTOs;
using NexSync.Application.Interfaces;
using NexSync.Application.Validators;
using NexSync.Domain.Entities;
using NexSync.Domain.Exceptions;

namespace NexSync.Application.Services;

public class FolderService(
    IFolderRepository folders,
    IFileRepository files,
    IStorageService storage,
    ITransactionProvider transactions,
    ISyncChangeWriter changes) : IFolderService
{
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

    public async Task<FolderDto> CreateAsync(Guid userId, string name, Guid? parentFolderId, Guid? deviceId = null)
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
        var folder = new Folder
        {
            Id = Guid.NewGuid(), Name = name, ParentFolderId = parentFolderId,
            OwnerId = userId, CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow
        };
        await using var tx = await transactions.BeginTransactionAsync();
        try
        {
            await folders.StageAsync(folder);
            await changes.WriteFolderChangeAsync(userId, folder.Id, SyncOperation.Created, name, parentFolderId, deviceId);
            await folders.SaveChangesAsync();
            await tx.CommitAsync();
        }
        catch
        {
            await tx.RollbackAsync();
            throw;
        }
        return Map(folder);
    }

    public async Task<FolderDto> UpdateAsync(Guid userId, Guid folderId, string name, Guid? parentFolderId, Guid? deviceId = null)
    {
        FileNameValidator.Validate(name);
        var folder = await folders.GetByIdAsync(folderId);
        if (folder is null || folder.OwnerId != userId)
            throw new ForbiddenException("Access denied.");
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
            await changes.WriteFolderChangeAsync(userId, folderId, op, name, parentFolderId, deviceId);
            await folders.SaveChangesAsync();
            await tx.CommitAsync();
        }
        catch
        {
            await tx.RollbackAsync();
            throw;
        }
        return Map(folder);
    }

    public async Task DeleteAsync(Guid userId, Guid folderId, Guid? deviceId = null)
    {
        var folder = await folders.GetByIdAsync(folderId);
        if (folder is null || folder.OwnerId != userId)
            throw new ForbiddenException("Access denied.");
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
                await changes.WriteFolderChangeAsync(userId, fid, SyncOperation.Deleted, f.Name, f.ParentFolderId, deviceId);
            }
            await folders.SaveChangesAsync();
            await tx.CommitAsync();
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
