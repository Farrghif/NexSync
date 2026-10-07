using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using NexSync.Application.DTOs;
using NexSync.Application.Interfaces;
using NexSync.Domain.Entities;
using NexSync.Domain.Exceptions;
using NexSync.Domain.Sync;
using NexSync.Infrastructure.Data;

namespace NexSync.Infrastructure.Repositories;

public class DeviceRepository(AppDbContext context) : Repository<Device>(context), IDeviceRepository
{
    private static readonly JsonSerializerOptions PayloadJson = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    public async Task<IReadOnlyList<Device>> GetByUserAsync(Guid userId)
        => await _context.Devices.Where(d => d.UserId == userId).OrderBy(d => d.CreatedAt).ToListAsync();

    public async Task<Device?> GetOwnedAsync(Guid userId, Guid deviceId)
        => await _context.Devices.FirstOrDefaultAsync(d => d.Id == deviceId && d.UserId == userId);

    public async Task<(Device Device, bool Replayed)> RegisterIdempotentAsync(Guid userId, string name, DevicePlatform platform, Guid operationId)
    {
        var fingerprint = RequestFingerprint.ForDeviceRegistration(name, platform.ToString());
        await using var tx = await _context.Database.BeginTransactionAsync();
        try
        {
            var existing = await _context.ProcessedOperations.FindAsync(operationId);
            if (existing is not null)
            {
                if (!string.Equals(existing.RequestFingerprint, fingerprint, StringComparison.Ordinal))
                    throw new OperationIdReuseException("X-Operation-Id was already used for a different request.");
                var replayed = existing.EntityId.HasValue
                    ? await _context.Devices.FindAsync(existing.EntityId.Value)
                    : null;
                if (replayed is null) throw new NotFoundException("Device not found.");
                await tx.CommitAsync();
                return (replayed, true);
            }
            var now = DateTime.UtcNow;
            var device = new Device
            {
                Id = Guid.NewGuid(),
                UserId = userId,
                Name = name,
                Platform = platform,
                CreatedAt = now,
                LastSeenAt = now,
                RevokedAt = null
            };
            _context.Devices.Add(device);
            _context.ProcessedOperations.Add(new ProcessedOperation
            {
                OperationId = operationId,
                UserId = userId,
                DeviceId = device.Id,
                RequestFingerprint = fingerprint,
                ResultStatus = 201,
                ResultPayload = JsonSerializer.Serialize(device.ToDto(), PayloadJson),
                EntityId = device.Id,
                Sequence = null,
                CreatedAt = now
            });
            await _context.SaveChangesAsync();
            await tx.CommitAsync();
            return (device, false);
        }
        catch (DbUpdateException)
        {
            await tx.RollbackAsync();
            _context.ChangeTracker.Clear();
            var raced = await _context.ProcessedOperations.FindAsync(operationId);
            if (raced is not null
                && string.Equals(raced.RequestFingerprint, fingerprint, StringComparison.Ordinal)
                && raced.EntityId.HasValue)
            {
                var dev = await _context.Devices.FindAsync(raced.EntityId.Value);
                if (dev is not null) return (dev, true);
            }
            throw;
        }
        catch
        {
            await tx.RollbackAsync();
            throw;
        }
    }
}
