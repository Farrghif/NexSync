using Microsoft.AspNetCore.Mvc;
using NexSync.Application.Interfaces;
using NexSync.Domain.Exceptions;

namespace NexSync.API.Controllers;

public static class IdempotencyHelper
{
    public static MutationContext? Parse(Microsoft.AspNetCore.Http.IHeaderDictionary headers, string fingerprint, int successStatus)
    {
        if (!headers.TryGetValue("X-Operation-Id", out var raw)) return null;
        if (!Guid.TryParse(raw.ToString(), out var operationId))
            throw new DomainException("X-Operation-Id header must be a valid UUID.");
        return new MutationContext(operationId, fingerprint, successStatus);
    }

    public static async Task<IActionResult?> ReplayIfSeenAsync(
        IIdempotencyStore store, Guid userId, MutationContext mutation, CancellationToken ct = default)
    {
        var existing = await store.FindAsync(mutation.OperationId, ct);
        if (existing is null) return null;
        if (existing.UserId != userId || !string.Equals(existing.RequestFingerprint, mutation.Fingerprint, StringComparison.Ordinal))
            throw new OperationIdReuseException("X-Operation-Id was already used for a different request.");
        if (string.IsNullOrEmpty(existing.ResultPayload))
            return new StatusCodeResult(existing.ResultStatus);
        return new ContentResult
        {
            StatusCode = existing.ResultStatus,
            Content = existing.ResultPayload,
            ContentType = "application/json"
        };
    }
}
