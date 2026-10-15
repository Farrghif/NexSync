namespace NexSync.Application.Interfaces;

public interface IOriginDeviceResolver
{
    Task<Guid?> GetOriginDeviceIdAsync(Guid entityId);
}
