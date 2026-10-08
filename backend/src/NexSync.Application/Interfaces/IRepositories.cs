using NexSync.Domain.Entities;

namespace NexSync.Application.Interfaces;

public interface IRepository<T> where T : class
{
    Task<T?> GetByIdAsync(Guid id);
    Task<IEnumerable<T>> GetAllAsync();
    Task AddAsync(T entity);
    Task UpdateAsync(T entity);
    Task DeleteAsync(T entity);
    Task SaveChangesAsync();
    Task StageAsync(T entity);
    void StageUpdate(T entity);
    void StageDelete(T entity);
}

public interface IUserRepository : IRepository<User>
{
    Task<User?> GetByEmailAsync(string email);
    Task<bool> EmailExistsAsync(string email);
}

public interface IFolderRepository : IRepository<Folder>
{
    Task<IEnumerable<Folder>> GetByParentAsync(Guid userId, Guid? parentId, int page, int pageSize);
    Task<int> GetCountByParentAsync(Guid userId, Guid? parentId);
    Task<bool> NameExistsInParentAsync(Guid userId, Guid? parentId, string name);
    Task<IEnumerable<Folder>> GetDescendantsAsync(Guid folderId);
}

public interface IFileRepository : IRepository<FileEntry>
{
    Task<IEnumerable<FileEntry>> GetByFolderAsync(Guid userId, Guid? folderId, int page, int pageSize);
    Task<int> GetCountByFolderAsync(Guid userId, Guid? folderId);
    Task<bool> NameExistsInFolderAsync(Guid userId, Guid? folderId, string name);
    Task<int> GetCountByStoragePathAsync(string storagePath);
}

public interface IRefreshTokenRepository : IRepository<RefreshToken>
{
    Task<RefreshToken?> GetByTokenHashAsync(string tokenHash);
    Task RevokeTokenFamilyAsync(Guid tokenId);
    Task<IEnumerable<RefreshToken>> GetActiveTokensByUserAsync(Guid userId);
}

public interface IDeviceRepository : IRepository<Device>
{
    Task<IReadOnlyList<Device>> GetByUserAsync(Guid userId);
    Task<Device?> GetOwnedAsync(Guid userId, Guid deviceId);
}
