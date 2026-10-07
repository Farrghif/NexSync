using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using NexSync.Application.DTOs;
using NexSync.Application.Interfaces;

namespace NexSync.API.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class FoldersController(IFolderService folders) : ControllerBase
{
    private Guid UserId => Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

    [HttpGet]
    public async Task<IActionResult> GetContents([FromQuery] Guid? parentId, [FromQuery] int page = 1, [FromQuery] int pageSize = 50)
        => Ok(await folders.GetContentsAsync(UserId, parentId, page, pageSize));

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> GetById(Guid id)
        => Ok(await folders.GetByIdAsync(UserId, id));

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateFolderRequest req)
    {
        var f = await folders.CreateAsync(UserId, req.Name, req.ParentFolderId);
        return CreatedAtAction(nameof(GetById), new { id = f.Id }, f);
    }

    [HttpPut("{id:guid}")]
    public async Task<IActionResult> Update(Guid id, [FromBody] UpdateFolderRequest req)
        => Ok(await folders.UpdateAsync(UserId, id, req.Name, req.ParentFolderId));

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id)
    {
        await folders.DeleteAsync(UserId, id);
        return NoContent();
    }
}
