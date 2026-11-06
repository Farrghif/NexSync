using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using NexSync.Application.DTOs;
using NexSync.Application.Interfaces;
using NexSync.Domain.Exceptions;
using NexSync.Domain.Sync;

namespace NexSync.API.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class DevicesController(IDeviceService devices, IIdempotencyStore idempotency) : ControllerBase
{
    private static readonly HashSet<string> ValidPlatforms = new(StringComparer.OrdinalIgnoreCase)
    {
        "Web", "Desktop", "Android", "iOS"
    };

    private Guid UserId
    {
        get
        {
            var userIdClaim = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (string.IsNullOrEmpty(userIdClaim) || !Guid.TryParse(userIdClaim, out var userId))
                throw new NexSync.Domain.Exceptions.UnauthorizedAccessException("Invalid or missing user claim.");
            return userId;
        }
    }

    [HttpGet]
    public async Task<IActionResult> List()
        => Ok(await devices.ListAsync(UserId));

    [HttpPost]
    public async Task<IActionResult> Register(
        [FromBody] RegisterDeviceRequest req,
        [FromHeader(Name = "X-Operation-Id")] string? operationId)
    {
        if (string.IsNullOrWhiteSpace(operationId) || !Guid.TryParse(operationId, out var opId))
            throw new DomainException("X-Operation-Id header with a valid UUID is required.");

        var deviceName = (req?.Name ?? "").Trim();
        if (string.IsNullOrWhiteSpace(deviceName) || deviceName.Length > 100)
            throw new DomainException("Device name must be between 1 and 100 characters.");

        var platform = (req?.Platform ?? "").Trim();
        if (string.IsNullOrWhiteSpace(platform))
            throw new DomainException("Platform is required.");
        if (!ValidPlatforms.Contains(platform))
            throw new DomainException($"Platform must be one of: {string.Join(", ", ValidPlatforms)}.");

        var mutation = new MutationContext(opId,
            RequestFingerprint.ForDeviceRegistration(deviceName, platform),
            StatusCodes.Status201Created);
        
        var replay = await IdempotencyHelper.ReplayIfSeenAsync(idempotency, UserId, mutation);
        if (replay is not null) return replay;

        var dto = await devices.RegisterAsync(UserId, deviceName, platform, opId);
        return StatusCode(StatusCodes.Status201Created, dto);
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Revoke(Guid id)
    {
        if (id == Guid.Empty)
            throw new DomainException("Invalid device ID.");
        
        await devices.RevokeAsync(UserId, id);
        return NoContent();
    }
}
