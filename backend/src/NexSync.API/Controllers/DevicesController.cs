using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using NexSync.Application.DTOs;
using NexSync.Application.Interfaces;
using NexSync.Domain.Exceptions;

namespace NexSync.API.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class DevicesController(IDeviceService devices) : ControllerBase
{
    private Guid UserId => Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

    [HttpGet]
    public async Task<IActionResult> List()
        => Ok(await devices.ListAsync(UserId));

    [HttpPost]
    public async Task<IActionResult> Register(
        [FromBody] RegisterDeviceRequest req,
        [FromHeader(Name = "X-Operation-Id")] string? operationId)
    {
        if (!Guid.TryParse(operationId, out var opId))
            throw new DomainException("X-Operation-Id header with a valid UUID is required.");
        var dto = await devices.RegisterAsync(UserId, req?.Name, req?.Platform, opId);
        return StatusCode(StatusCodes.Status201Created, dto);
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Revoke(Guid id)
    {
        await devices.RevokeAsync(UserId, id);
        return NoContent();
    }
}
