using System.Text.Json;
using NexSync.Application.DTOs;
using NexSync.Application.Interfaces;
using NexSync.Domain.Entities;
using NexSync.Domain.Exceptions;
using NexSync.Domain.Sync;

namespace NexSync.Application.Services;

public class DeviceService(
    IDeviceRepository devices,
    ITransactionProvider transactions,
    IIdempotencyStore idempotency) : IDeviceService
{
    private static readonly JsonSerializerOptions PayloadJson = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    public async Task<DeviceDto> RegisterAsync(Guid userId, string? name, string? platform, Guid operationId)
    {
        var trimmed = (name ?? "").Trim();
        if (trimmed.Length == 0 || trimmed.Length > 100)
            throw new DomainException("Device name must be 1-100 characters.");
        var pf = (platform ?? "").Trim();
        if (!Enum.GetNames<DevicePlatform>().Any(n => string.Equals(n, pf, StringComparison.OrdinalIgnoreCase)))
            throw new DomainException("Platform must be one of: Windows, Android, Web.");
        var parsed = Enum.Parse<DevicePlatform>(pf, ignoreCase: true);
        var fingerprint = RequestFingerprint.ForDeviceRegistration(trimmed, parsed.ToString());
        var mutation = new MutationContext(operationId, fingerprint, StatusCodes201);
        var replay = await idempotency.FindAsync(operationId);
        if (replay is not null)
        {
            if (replay.UserId != userId || !string.Equals(replay.RequestFingerprint, fingerprint, StringComparison.Ordinal))
                throw new OperationIdReuseException("X-Operation-Id was already used for a different request.");
            return Deserialize(replay.ResultPayload);
        }
        await using var tx = await transactions.BeginTransactionAsync();
        try
        {
            var now = DateTime.UtcNow;
            var device = new Device
            {
                Id = Guid.NewGuid(),
                UserId = userId,
                Name = trimmed,
                Platform = parsed,
                CreatedAt = now,
                LastSeenAt = now,
                RevokedAt = null
            };
            await devices.StageAsync(device);
            var dto = device.ToDto();
            await idempotency.StageAsync(operationId, userId, device.Id, fingerprint, StatusCodes201,
                JsonSerializer.Serialize(dto, PayloadJson), device.Id, null);
            await devices.SaveChangesAsync();
            await tx.CommitAsync();
            return dto;
        }
        catch (Exception)
        {
            await tx.RollbackAsync();
            var raced = await idempotency.FindAsync(operationId);
            if (raced is not null
                && raced.UserId == userId
                && string.Equals(raced.RequestFingerprint, fingerprint, StringComparison.Ordinal))
                return Deserialize(raced.ResultPayload);
            throw;
        }
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

    public async Task<Guid> ValidateAsync(Guid userId, Guid deviceId)
    {
        var d = await devices.GetOwnedAsync(userId, deviceId);
        if (d is null || d.RevokedAt is not null)
            throw new ForbiddenException("Device is invalid, revoked, or not owned by user.");
        return d.Id;
    }

    private const int StatusCodes201 = 201;

    private static DeviceDto Deserialize(string? payload)
        => JsonSerializer.Deserialize<DeviceDto>(payload!, PayloadJson)
            ?? throw new NotFoundException("Device not found.");
}

