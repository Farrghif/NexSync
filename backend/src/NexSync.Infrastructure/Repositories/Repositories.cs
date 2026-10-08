using Microsoft.EntityFrameworkCore;
using NexSync.Application.Interfaces;
using NexSync.Domain.Entities;
using NexSync.Infrastructure.Data;

namespace NexSync.Infrastructure.Repositories;

public class Repository<T>(AppDbContext context) : IRepository<T> where T : class
{
    protected readonly AppDbContext _context = context;

    public async Task<T?> GetByIdAsync(Guid id) => await _context.Set<T>().FindAsync(id);
    public async Task<IEnumerable<T>> GetAllAsync() => await _context.Set<T>().ToListAsync();
    public async Task AddAsync(T entity) { await _context.Set<T>().AddAsync(entity); await SaveChangesAsync(); }
    public async Task UpdateAsync(T entity) { _context.Set<T>().Update(entity); await SaveChangesAsync(); }
    public async Task DeleteAsync(T entity) { _context.Set<T>().Remove(entity); await SaveChangesAsync(); }
    public async Task SaveChangesAsync() => await _context.SaveChangesAsync();
    public async Task StageAsync(T entity) => await _context.Set<T>().AddAsync(entity);
    public void StageUpdate(T entity) => _context.Set<T>().Update(entity);
    public void StageDelete(T entity) => _context.Set<T>().Remove(entity);
}

public class UserRepository(AppDbContext context) : Repository<User>(context), IUserRepository
{
    public async Task<User?> GetByEmailAsync(string email)
        => await _context.Users.FirstOrDefaultAsync(u => u.Email.ToLower() == email.ToLower());
    public async Task<bool> EmailExistsAsync(string email)
        => await _context.Users.AnyAsync(u => u.Email.ToLower() == email.ToLower());
}

public class FolderRepository(AppDbContext context) : Repository<Folder>(context), IFolderRepository
{
    public async Task<IEnumerable<Folder>> GetByParentAsync(Guid userId, Guid? parentId, int page, int pageSize)
        => await _context.Folders.Where(f => f.OwnerId == userId && f.ParentFolderId == parentId)
            .OrderBy(f => f.Name.ToLower()).Skip((page - 1) * pageSize).Take(pageSize).ToListAsync();

    public async Task<int> GetCountByParentAsync(Guid userId, Guid? parentId)
        => await _context.Folders.CountAsync(f => f.OwnerId == userId && f.ParentFolderId == parentId);

    public async Task<bool> NameExistsInParentAsync(Guid userId, Guid? parentId, string name)
        => await _context.Folders.AnyAsync(f => f.OwnerId == userId && f.ParentFolderId == parentId && f.Name.ToLower() == name.ToLower());

    public async Task<IEnumerable<Folder>> GetDescendantsAsync(Guid folderId)
    {
        var result = new List<Folder>();
        var queue = new Queue<Guid>();
        queue.Enqueue(folderId);
        while (queue.Count > 0)
        {
            var current = queue.Dequeue();
            var children = await _context.Folders.Where(f => f.ParentFolderId == current).ToListAsync();
            foreach (var c in children) { result.Add(c); queue.Enqueue(c.Id); }
        }
        return result;
    }
}

public class FileRepository(AppDbContext context) : Repository<FileEntry>(context), IFileRepository
{
    public async Task<IEnumerable<FileEntry>> GetByFolderAsync(Guid userId, Guid? folderId, int page, int pageSize)
        => await _context.Files.Where(f => f.OwnerId == userId && f.FolderId == folderId)
            .OrderBy(f => f.Name.ToLower()).Skip((page - 1) * pageSize).Take(pageSize).ToListAsync();

    public async Task<int> GetCountByFolderAsync(Guid userId, Guid? folderId)
        => await _context.Files.CountAsync(f => f.OwnerId == userId && f.FolderId == folderId);

    public async Task<bool> NameExistsInFolderAsync(Guid userId, Guid? folderId, string name)
        => await _context.Files.AnyAsync(f => f.OwnerId == userId && f.FolderId == folderId && f.Name.ToLower() == name.ToLower());

    public async Task<int> GetCountByStoragePathAsync(string storagePath)
        => await _context.Files.CountAsync(f => f.StoragePath == storagePath);
}

public class RefreshTokenRepository(AppDbContext context) : Repository<RefreshToken>(context), IRefreshTokenRepository
{
    public async Task<RefreshToken?> GetByTokenHashAsync(string tokenHash)
        => await _context.RefreshTokens.Include(t => t.User).FirstOrDefaultAsync(t => t.TokenHash == tokenHash);

    public async Task RevokeTokenFamilyAsync(Guid tokenId)
    {
        var start = await _context.RefreshTokens.FindAsync(tokenId);
        if (start is null) return;
        var current = start;
        var visited = new HashSet<Guid>();
        while (current is not null && visited.Add(current.Id))
        {
            current.RevokedAt ??= DateTime.UtcNow;
            if (current.ReplacedByTokenId is null) break;
            current = await _context.RefreshTokens.FindAsync(current.ReplacedByTokenId);
        }
        var chain = await _context.RefreshTokens.Where(t => t.ReplacedByTokenId == tokenId).ToListAsync();
        foreach (var t in chain) t.RevokedAt ??= DateTime.UtcNow;
        await _context.SaveChangesAsync();
    }

    public async Task<IEnumerable<RefreshToken>> GetActiveTokensByUserAsync(Guid userId)
        => await _context.RefreshTokens.Where(t => t.UserId == userId && t.RevokedAt == null && t.ExpiresAt > DateTime.UtcNow).ToListAsync();
}
