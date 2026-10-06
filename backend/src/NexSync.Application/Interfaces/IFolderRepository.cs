using NexSync.Domain.Entities;

namespace NexSync.Application.Interfaces;

public interface IFolderRepository : IRepository<Folder>
{
    Task<Folder?> GetByIdWithOwnerAsync(Guid id);
    Task<IEnumerable<Folder>> GetByParentAsync(Guid userId, Guid? parentId, int page, int pageSize);
    Task<int> GetCountByParentAsync(Guid userId, Guid? parentId);
    Task<bool> NameExistsInParentAsync(Guid userId, Guid? parentId, string name);
    Task<IEnumerable<Folder>> GetDescendantsAsync(Guid folderId);
}
