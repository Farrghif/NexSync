using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using NexSync.Application.DTOs;
using NexSync.Application.Interfaces;

namespace NexSync.API.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class FilesController(IFileService files) : ControllerBase
{
    private Guid UserId => Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

    [HttpPost("upload")]
    [RequestSizeLimit(104_857_600)]
    public async Task<IActionResult> Upload(IFormFile file, [FromQuery] Guid? folderId)
    {
        if (file is null || file.Length == 0) return BadRequest(new { message = "No file provided." });
        await using var stream = file.OpenReadStream();
        var res = await files.UploadAsync(UserId, stream, file.FileName, file.ContentType, folderId);
        return CreatedAtAction(nameof(GetById), new { id = res.Id }, res);
    }

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> GetById(Guid id)
        => Ok(await files.GetByIdAsync(UserId, id));

    [HttpGet("{id:guid}/download")]
    public async Task<IActionResult> Download(Guid id)
    {
        var meta = await files.GetByIdAsync(UserId, id);
        var stream = await files.DownloadAsync(UserId, id);
        return File(stream, meta.ContentType, meta.Name);
    }

    [HttpPut("{id:guid}")]
    public async Task<IActionResult> Update(Guid id, [FromBody] UpdateFileRequest req)
        => Ok(await files.UpdateAsync(UserId, id, req.Name, req.FolderId));

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id)
    {
        await files.DeleteAsync(UserId, id);
        return NoContent();
    }
}
