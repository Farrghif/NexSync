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

}
