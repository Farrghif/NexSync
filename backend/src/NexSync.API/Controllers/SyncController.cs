using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using NexSync.Application.Interfaces;
using NexSync.Domain.Exceptions;

namespace NexSync.API.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class SyncController(ISyncService sync, IDeviceService devices) : ControllerBase
{
    private Guid UserId => Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

    private async Task<Guid> RequireDeviceAsync()
    {
        if (!Request.Headers.TryGetValue("X-Device-Id", out var raw)
            || !Guid.TryParse(raw.ToString(), out var deviceId))
            throw new DomainException("X-Device-Id header with a valid UUID is required.");
        await devices.ValidateAsync(UserId, deviceId);
        return deviceId;
    }

    [HttpGet("pull")]
    public async Task<IActionResult> Pull([FromQuery] long since = 0, [FromQuery] long? until = null, [FromQuery] int limit = 500)
    {
        await RequireDeviceAsync();
        if (until is null)
            throw new DomainException("Query parameter 'until' is required.");
        return Ok(await sync.PullAsync(UserId, since, until.Value, limit));
    }

    [HttpGet("cursor")]
    public async Task<IActionResult> Cursor()
    {
        await RequireDeviceAsync();
        return Ok(await sync.GetCursorAsync(UserId));
    }
}
