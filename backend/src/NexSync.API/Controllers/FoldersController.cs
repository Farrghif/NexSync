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
public class FoldersController(IFolderService folders, IDeviceService devices, IIdempotencyStore idempotency) : ControllerBase
{
    private Guid UserId => Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

    private async Task<Guid?> ResolveDeviceAsync()
    {
        if (!Request.Headers.TryGetValue("X-Device-Id", out var raw)) return null;
        if (!Guid.TryParse(raw.ToString(), out var deviceId))
            throw new DomainException("X-Device-Id header must be a valid UUID.");
        await devices.ValidateAsync(UserId, deviceId);
        return deviceId;
    }

    [HttpGet]
    public async Task<IActionResult> GetContents([FromQuery] Guid? parentId, [FromQuery] int page = 1, [FromQuery] int pageSize = 50)
        => Ok(await folders.GetContentsAsync(UserId, parentId, page, pageSize));

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> GetById(Guid id)
        => Ok(await folders.GetByIdAsync(UserId, id));

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateFolderRequest req)
    {
        var deviceId = await ResolveDeviceAsync();
        var mutation = IdempotencyHelper.Parse(Request.Headers,
            RequestFingerprint.ForMutation("POST", "/api/folders",
                ("name", req.Name), ("parentFolderId", req.ParentFolderId?.ToString())),
            StatusCodes.Status201Created);
        if (mutation is not null)
        {
            var replay = await IdempotencyHelper.ReplayIfSeenAsync(idempotency, UserId, mutation);
            if (replay is not null) return replay;
        }
        var f = await folders.CreateAsync(UserId, req.Name, req.ParentFolderId, deviceId, mutation);
        return CreatedAtAction(nameof(GetById), new { id = f.Id }, f);
    }

    [HttpPut("{id:guid}")]
    public async Task<IActionResult> Update(Guid id, [FromBody] UpdateFolderRequest req)
    {
        var deviceId = await ResolveDeviceAsync();
        var mutation = IdempotencyHelper.Parse(Request.Headers,
            RequestFingerprint.ForMutation("PUT", $"/api/folders/{id}",
                ("name", req.Name), ("parentFolderId", req.ParentFolderId?.ToString())),
            StatusCodes.Status200OK);
        if (mutation is not null)
        {
            var replay = await IdempotencyHelper.ReplayIfSeenAsync(idempotency, UserId, mutation);
            if (replay is not null) return replay;
        }
        return Ok(await folders.UpdateAsync(UserId, id, req.Name, req.ParentFolderId, deviceId, mutation));
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id)
    {
        var deviceId = await ResolveDeviceAsync();
        var mutation = IdempotencyHelper.Parse(Request.Headers,
            RequestFingerprint.ForMutation("DELETE", $"/api/folders/{id}"),
            StatusCodes.Status204NoContent);
        if (mutation is not null)
        {
            var replay = await IdempotencyHelper.ReplayIfSeenAsync(idempotency, UserId, mutation);
            if (replay is not null) return replay;
        }
        await folders.DeleteAsync(UserId, id, deviceId, mutation);
        return NoContent();
    }
}
