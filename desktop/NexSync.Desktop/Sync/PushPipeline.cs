using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using NexSync.Desktop.Auth;
using NexSync.Desktop.Data;

namespace NexSync.Desktop.Sync;

public sealed class PushPipeline(
    LocalDb db,
    PendingQueueService queue,
    DesktopAuthService auth,
    string localRoot)
{
    private readonly LocalFolderRepository _folders = new(db.Connection);
    private readonly LocalFileRepository _files = new(db.Connection);
    private readonly ProcessedOperationRepository _processed = new(db.Connection);

    public async Task<PushOutcome> PushAsync(PendingOperationRow op, CancellationToken ct = default)
    {
        try
        {
            return op.OpType switch
            {
                PendingOpType.CreateFolder => await PushCreateFolderAsync(op, ct),
                PendingOpType.CreateFile or PendingOpType.ModifyFile => await PushUploadAsync(op, ct),
                PendingOpType.DeleteFolder => await PushDeleteAsync(op, isFile: false, ct),
                PendingOpType.DeleteFile => await PushDeleteAsync(op, isFile: true, ct),
                PendingOpType.MoveFolder => await PushMoveAsync(op, isFile: false, ct),
                PendingOpType.MoveFile or PendingOpType.RenameFolder => await PushMoveAsync(op, isFile: true, ct),
                _ => PushOutcome.TransientFailure
            };
        }
        catch (HttpRequestException)
        {
            return PushOutcome.TransientFailure;
        }
        catch (TaskCanceledException)
        {
            return PushOutcome.TransientFailure;
        }
    }

    private async Task<PushOutcome> PushCreateFolderAsync(PendingOperationRow op, CancellationToken ct)
    {
        var name = LeafName(op.RelPath);
        var parentServerId = ResolveParentServerId(op.RelPath);
        using var req = new HttpRequestMessage(HttpMethod.Post, "api/folders")
        {
            Content = JsonContent.Create(new { name, parentFolderId = parentServerId })
        };
        req.Headers.Add("X-Operation-Id", op.OperationId.ToString());
        using var res = await auth.SendWithAuthAsync(req, allowRefresh: true, ct);
        return await HandleMutationResponseAsync(op, res,
            onSuccess: async doc =>
            {
                var id = doc.RootElement.GetProperty("id").GetGuid();
                using var tx = db.BeginTransaction();
                var existing = _folders.GetByPath(op.RelPath, tx);
                _folders.Upsert(new LocalFolderRow(op.RelPath,
                    id,
                    existing?.ParentRelativePath,
                    existing?.LastAppliedSequence ?? 0,
                    DateTime.UtcNow), tx);
                queue.Complete(op, success: true, tx);
                _processed.Record(new ProcessedOperationRow(op.OperationId, $"201 folder {id}", DateTime.UtcNow), tx);
                tx.Commit();
            }, ct);
    }

    private async Task<PushOutcome> PushUploadAsync(PendingOperationRow op, CancellationToken ct)
    {
        var fullPath = Path.Combine(localRoot, op.RelPath.Replace('/', Path.DirectorySeparatorChar));
        if (!File.Exists(fullPath))
            return PushOutcome.TransientFailure;
        var parentServerId = ResolveParentServerId(op.RelPath);
        using var form = new MultipartFormDataContent();
        await using var stream = new FileStream(fullPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        var content = new StreamContent(stream);
        content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/octet-stream");
        form.Add(content, "file", LeafName(op.RelPath));
        var url = parentServerId.HasValue ? $"api/files/upload?folderId={parentServerId}" : "api/files/upload";
        using var req = new HttpRequestMessage(HttpMethod.Post, url) { Content = form };
        req.Headers.Add("X-Operation-Id", op.OperationId.ToString());
        using var res = await auth.SendWithAuthAsync(req, allowRefresh: true, ct);
        return await HandleMutationResponseAsync(op, res,
            onSuccess: async doc =>
            {
                var root = doc.RootElement;
                var id = root.GetProperty("id").GetGuid();
                var hash = root.TryGetProperty("hash", out var h) ? h.GetString() : null;
                var size = root.TryGetProperty("size", out var s) ? s.GetInt64() : (long?)null;
                using var tx = db.BeginTransaction();
                var existing = _files.GetByPath(op.RelPath, tx);
                _files.Upsert(new LocalFileRow(op.RelPath, id,
                    existing?.ServerFolderId ?? parentServerId,
                    hash ?? existing?.Hash, size ?? existing?.Size,
                    existing?.LastAppliedSequence ?? 0, DateTime.UtcNow), tx);
                queue.Complete(op, success: true, tx);
                _processed.Record(new ProcessedOperationRow(op.OperationId, $"200 file {id}", DateTime.UtcNow), tx);
                tx.Commit();
            }, ct);
    }

    private async Task<PushOutcome> PushDeleteAsync(PendingOperationRow op, bool isFile, CancellationToken ct)
    {
        if (!op.ServerId.HasValue)
            return PushOutcome.TransientFailure;
        var url = isFile ? $"api/files/{op.ServerId}" : $"api/folders/{op.ServerId}";
        using var req = new HttpRequestMessage(HttpMethod.Delete, url);
        req.Headers.Add("X-Operation-Id", op.OperationId.ToString());
        using var res = await auth.SendWithAuthAsync(req, allowRefresh: true, ct);
        if (res.StatusCode == HttpStatusCode.NotFound)
        {
            queue.Complete(op, success: true);
            return PushOutcome.Pushed;
        }
        return await HandleEmptyMutationResponseAsync(op, res,
            onSuccess: () =>
            {
                using var tx = db.BeginTransaction();
                queue.Complete(op, success: true, tx);
                tx.Commit();
            }, ct);
    }

    private async Task<PushOutcome> PushMoveAsync(PendingOperationRow op, bool isFile, CancellationToken ct)
    {
        if (!op.ServerId.HasValue || op.NewRelPath is null)
            return PushOutcome.TransientFailure;
        var newName = LeafName(op.NewRelPath);
        Guid? newParentId = ResolveParentServerId(op.NewRelPath);
        var url = isFile ? $"api/files/{op.ServerId}" : $"api/folders/{op.ServerId}";
        JsonContent content = isFile
            ? JsonContent.Create(new UpdateFileBody(newName, newParentId))
            : JsonContent.Create(new UpdateFolderBody(newName, newParentId));
        using var req = new HttpRequestMessage(HttpMethod.Put, url)
        {
            Content = content
        };
        req.Headers.Add("X-Operation-Id", op.OperationId.ToString());
        using var res = await auth.SendWithAuthAsync(req, allowRefresh: true, ct);
        return await HandleMutationResponseAsync(op, res,
            onSuccess: _ =>
            {
                using var tx = db.BeginTransaction();
                if (isFile)
                {
                    var existing = _files.GetByPath(op.RelPath, tx);
                    if (existing is not null)
                    {
                        _files.Delete(op.RelPath, tx);
                        _files.Upsert(new LocalFileRow(op.NewRelPath, existing.ServerFileId,
                            newParentId ?? existing.ServerFolderId, existing.Hash, existing.Size,
                            existing.LastAppliedSequence, DateTime.UtcNow), tx);
                    }
                }
                else
                {
                    var existing = _folders.GetByPath(op.RelPath, tx);
                    if (existing is not null)
                    {
                        _folders.Delete(op.RelPath, tx);
                        _folders.Upsert(new LocalFolderRow(op.NewRelPath, existing.ServerFolderId,
                            ParentOf(op.NewRelPath), existing.LastAppliedSequence, DateTime.UtcNow), tx);
                    }
                }
                queue.Complete(op, success: true, tx);
                _processed.Record(new ProcessedOperationRow(op.OperationId, "200 move", DateTime.UtcNow), tx);
                tx.Commit();
                return Task.CompletedTask;
            }, ct);
    }

    private async Task<PushOutcome> HandleEmptyMutationResponseAsync(
        PendingOperationRow op,
        HttpResponseMessage res,
        Action onSuccess,
        CancellationToken ct)
    {
        switch ((int)res.StatusCode)
        {
            case 200:
            case 201:
            case 204:
                onSuccess();
                return PushOutcome.Pushed;
            case 401:
                return PushOutcome.AuthFailed;
            case 403:
                return PushOutcome.Forbidden;
            case 404:
                return PushOutcome.NeedsReconcile;
            case 409:
                return PushOutcome.Conflict;
            default:
                if ((int)res.StatusCode == 429 || (int)res.StatusCode >= 500)
                    return PushOutcome.TransientFailure;
                queue.Complete(op, success: false);
                return PushOutcome.TransientFailure;
        }
    }

    private async Task<PushOutcome> HandleMutationResponseAsync(
        PendingOperationRow op,
        HttpResponseMessage res,
        Func<JsonDocument, Task> onSuccess,
        CancellationToken ct)
    {
        switch ((int)res.StatusCode)
        {
            case 200:
            case 201:
            case 204:
                using (var doc = JsonDocument.Parse(await res.Content.ReadAsStringAsync(ct)))
                    await onSuccess(doc);
                return PushOutcome.Pushed;
            case 401:
                return PushOutcome.AuthFailed;
            case 403:
                return PushOutcome.Forbidden;
            case 404:
                return PushOutcome.NeedsReconcile;
            case 409:
                return PushOutcome.Conflict;
            default:
                if ((int)res.StatusCode == 429 || (int)res.StatusCode >= 500)
                    return PushOutcome.TransientFailure;
                queue.Complete(op, success: false);
                return PushOutcome.TransientFailure;
        }
    }

    private sealed record UpdateFileBody(string Name, Guid? FolderId);
    private sealed record UpdateFolderBody(string Name, Guid? ParentFolderId);

    private Guid? ResolveParentServerId(string relPath)
    {
        var parent = ParentOf(relPath);
        if (parent is null) return null;
        return _folders.GetByPath(parent)?.ServerFolderId;
    }

    private static string? ParentOf(string relPath)
    {
        var idx = relPath.LastIndexOf('/');
        return idx <= 0 ? null : relPath[..idx];
    }

    private static string LeafName(string relPath)
    {
        var idx = relPath.LastIndexOf('/');
        return idx < 0 ? relPath : relPath[(idx + 1)..];
    }
}
