using Microsoft.EntityFrameworkCore;
using NexSync.Application.Interfaces;
using NexSync.Domain.Entities;
using NexSync.Infrastructure.Data;
using File = NexSync.Domain.Entities.File;

namespace NexSync.Infrastructure.Repositories;

public class FileRepository : Repository<File>, IFileRepository
{
    public FileRepository(AppDbContext context) : base(context) { }

    public async Task<File?> GetByIdWithOwnerAsync(Guid id)
    {
        return await _context.Files
            .Include(f => f.Owner)
            .FirstOrDefaultAsync(f => f.Id == id);
    }

    public async Task<IEnumerable<File>> GetByFolderAsync(Guid userId, Guid? folderId, int page, int pageSize)
    {
        return await _context.Files
            .Where(f => f.OwnerId == userId && f.FolderId == folderId)
            .OrderBy(f => f.Name)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync();
    }

    public async Task<int> GetCountByFolderAsync(Guid userId, Guid? folderId)
    {
        return await _context.Files
            .CountAsync(f => f.OwnerId == userId && f.FolderId == folderId);
    }

    public async Task<bool> NameExistsInFolderAsync(Guid userId, Guid? folderId, string name)
    {
        return await _context.Files
            .AnyAsync(f => f.OwnerId == userId && f.FolderId == folderId && f.Name.ToLower() == name.ToLower());
    }

    public async Task<int> GetCountByStoragePathAsync(string storagePath)
    {
        return await _context.Files
            .CountAsync(f => f.StoragePath == storagePath);
    }
}
