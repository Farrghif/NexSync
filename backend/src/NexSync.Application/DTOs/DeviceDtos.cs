using NexSync.Domain.Entities;

namespace NexSync.Application.DTOs;

public record RegisterDeviceRequest(string Name, string Platform);
public record DeviceDto(Guid DeviceId, string Name, string Platform, string Status, DateTime CreatedAt, DateTime LastSeenAt, DateTime? RevokedAt);

public static class DeviceMapping
{
    public static DeviceDto ToDto(this Device d) => new(
        d.Id,
        d.Name,
        d.Platform.ToString(),
        d.RevokedAt is null ? "ACTIVE" : "REVOKED",
        d.CreatedAt,
        d.LastSeenAt,
        d.RevokedAt);
}
