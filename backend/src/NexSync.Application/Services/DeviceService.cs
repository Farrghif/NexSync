using NexSync.Application.DTOs;
using NexSync.Application.Interfaces;
using NexSync.Domain.Entities;
using NexSync.Domain.Exceptions;

namespace NexSync.Application.Services;

public class DeviceService(IDeviceRepository devices) : IDeviceService
{
    public async Task<DeviceDto> RegisterAsync(Guid userId, string? name, string? platform, Guid operationId)
    {
        var trimmed = (name ?? "").Trim();
        if (trimmed.Length == 0 || trimmed.Length > 100)
            throw new DomainException("Device name must be 1-100 characters.");
        var pf = (platform ?? "").Trim();
        if (!Enum.GetNames<DevicePlatform>().Any(n => string.Equals(n, pf, StringComparison.OrdinalIgnoreCase)))
            throw new DomainException("Platform must be one of: Windows, Android, Web.");
        var parsed = Enum.Parse<DevicePlatform>(pf, ignoreCase: true);
        var (device, _) = await devices.RegisterIdempotentAsync(userId, trimmed, parsed, operationId);
        return device.ToDto();
    }

    public async Task<IReadOnlyList<DeviceDto>> ListAsync(Guid userId)
        => (await devices.GetByUserAsync(userId)).Select(d => d.ToDto()).ToList();

    public async Task RevokeAsync(Guid userId, Guid deviceId)
    {
        var d = await devices.GetOwnedAsync(userId, deviceId);
        if (d is null) throw new NotFoundException("Device not found.");
        if (d.RevokedAt is null)
        {
            d.RevokedAt = DateTime.UtcNow;
            await devices.UpdateAsync(d);
        }
    }
}
