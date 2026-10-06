using Microsoft.EntityFrameworkCore;
using NexSync.Application.Interfaces;
using NexSync.Domain.Entities;
using NexSync.Infrastructure.Data;

namespace NexSync.Infrastructure.Repositories;

public class FolderRepository : Repository<Folder>, IFolderRepository
{
    public FolderRepository(AppDbContext context) : base(context) { }

    public async Task<Folder?> GetByIdWithOwnerAsync(Guid id)
    {
        return await _context.Folders
            .Include(f => f.Owner)
            .FirstOrDefaultAsync(f => f.Id == id);
    }

    public async Task<IEnumerable<Folder>> GetByParentAsync(Guid userId, Guid? parentId, int page, int pageSize)
    {
        return await _context.Folders
            .Where(f => f.OwnerId == userId && f.ParentFolderId == parentId)
            .OrderBy(f => f.Name)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync();
    }

    public async Task<int> GetCountByParentAsync(Guid userId, Guid? parentId)
    {
        return await _context.Folders
            .CountAsync(f => f.OwnerId == userId && f.ParentFolderId == parentId);
    }

    public async Task<bool> NameExistsInParentAsync(Guid userId, Guid? parentId, string name)
    {
        return await _context.Folders
            .AnyAsync(f => f.OwnerId == userId && f.ParentFolderId == parentId && f.Name.ToLower() == name.ToLower());
    }

    public async Task<IEnumerable<Folder>> GetDescendantsAsync(Guid folderId)
    {
        var descendants = new List<Folder>();
        var queue = new Queue<Guid>();
        queue.Enqueue(folderId);

        while (queue.Count > 0)
        {
            var currentId = queue.Dequeue();
            var children = await _context.Folders
                .Where(f => f.ParentFolderId == currentId)
                .ToListAsync();

            foreach (var child in children)
            {
                descendants.Add(child);
                queue.Enqueue(child.Id);
            }
        }

        return descendants;
    }
}
