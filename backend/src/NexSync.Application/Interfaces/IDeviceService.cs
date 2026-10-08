using NexSync.Application.DTOs;

namespace NexSync.Application.Interfaces;

public interface IDeviceService
{
    Task<DeviceDto> RegisterAsync(Guid userId, string? name, string? platform, Guid operationId);
    Task<IReadOnlyList<DeviceDto>> ListAsync(Guid userId);
    Task RevokeAsync(Guid userId, Guid deviceId);
    Task<Guid> ValidateAsync(Guid userId, Guid deviceId);
}
