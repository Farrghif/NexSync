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
public class FilesController(IFileService files, IDeviceService devices, IIdempotencyStore idempotency) : ControllerBase
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

    [HttpPost("upload")]
    [RequestSizeLimit(104_857_600)]
    public async Task<IActionResult> Upload(IFormFile file, [FromQuery] Guid? folderId)
    {
        if (file is null || file.Length == 0) return BadRequest(new { message = "No file provided." });
        var deviceId = await ResolveDeviceAsync();
        MutationContext? mutation = null;
        if (Request.Headers.TryGetValue("X-Operation-Id", out var rawOp))
        {
            if (!Guid.TryParse(rawOp.ToString(), out var opId))
                throw new DomainException("X-Operation-Id header must be a valid UUID.");
            mutation = new MutationContext(opId, string.Empty, StatusCodes.Status201Created);
        }
        await using var stream = file.OpenReadStream();
        var res = await files.UploadAsync(UserId, stream, file.FileName, file.ContentType, folderId, deviceId, mutation);
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
    {
        var deviceId = await ResolveDeviceAsync();
        var mutation = IdempotencyHelper.Parse(Request.Headers,
            RequestFingerprint.ForMutation("PUT", $"/api/files/{id}",
                ("name", req.Name), ("folderId", req.FolderId?.ToString())),
            StatusCodes.Status200OK);
        if (mutation is not null)
        {
            var replay = await IdempotencyHelper.ReplayIfSeenAsync(idempotency, UserId, mutation);
            if (replay is not null) return replay;
        }
        return Ok(await files.UpdateAsync(UserId, id, req.Name, req.FolderId, deviceId, mutation));
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id)
    {
        var deviceId = await ResolveDeviceAsync();
        var mutation = IdempotencyHelper.Parse(Request.Headers,
            RequestFingerprint.ForMutation("DELETE", $"/api/files/{id}"),
            StatusCodes.Status204NoContent);
        if (mutation is not null)
        {
            var replay = await IdempotencyHelper.ReplayIfSeenAsync(idempotency, UserId, mutation);
            if (replay is not null) return replay;
        }
        await files.DeleteAsync(UserId, id, deviceId, mutation);
        return NoContent();
    }
}
