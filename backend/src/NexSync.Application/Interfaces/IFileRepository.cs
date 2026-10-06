using NexSync.Domain.Entities;
using File = NexSync.Domain.Entities.File;

namespace NexSync.Application.Interfaces;

public interface IFileRepository : IRepository<File>
{
    Task<File?> GetByIdWithOwnerAsync(Guid id);
    Task<IEnumerable<File>> GetByFolderAsync(Guid userId, Guid? folderId, int page, int pageSize);
    Task<int> GetCountByFolderAsync(Guid userId, Guid? folderId);
    Task<bool> NameExistsInFolderAsync(Guid userId, Guid? folderId, string name);
    Task<int> GetCountByStoragePathAsync(string storagePath);
}
