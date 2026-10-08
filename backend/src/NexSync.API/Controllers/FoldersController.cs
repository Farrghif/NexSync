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
public class FoldersController(IFolderService folders, IDeviceService devices) : ControllerBase
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
        var f = await folders.CreateAsync(UserId, req.Name, req.ParentFolderId, await ResolveDeviceAsync());
        return CreatedAtAction(nameof(GetById), new { id = f.Id }, f);
    }

    [HttpPut("{id:guid}")]
    public async Task<IActionResult> Update(Guid id, [FromBody] UpdateFolderRequest req)
        => Ok(await folders.UpdateAsync(UserId, id, req.Name, req.ParentFolderId, await ResolveDeviceAsync()));

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id)
    {
        await folders.DeleteAsync(UserId, id, await ResolveDeviceAsync());
        return NoContent();
    }
}
