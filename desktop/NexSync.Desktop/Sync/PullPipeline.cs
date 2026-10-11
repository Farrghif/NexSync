using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using NexSync.Desktop.Auth;
using NexSync.Desktop.Data;

namespace NexSync.Desktop.Sync;

public enum PullApplyOutcome
{
    Applied,
    AuthFailed,
    Forbidden,
    TransientFailure
}

public sealed record PullWindowResult(
    IReadOnlyList<RemoteChange> Changes,
    long NextCursor,
    bool HasMore,
    long HighWaterCursor);

public sealed record RemoteChange(
    long Sequence,
    string EntityType,
    Guid EntityId,
    string Operation,
    string? Name,
    Guid? ParentFolderId,
    string? Hash,
    long? Size,
    string? ContentType,
    Guid? OriginDeviceId);

public sealed class PullPipeline(
    LocalDb db,
    PendingQueueService queue,
    DesktopAuthService auth,
    string localRoot,
    IRemoteWriteTracker? suppression = null,
    ConflictHandler? conflicts = null)
{
    private readonly LocalFolderRepository _folders = new(db.Connection);
    private readonly LocalFileRepository _files = new(db.Connection);
    private readonly SyncStateRepository _state = new(db.Connection);
    private readonly IRemoteWriteTracker _suppression = suppression ?? NullRemoteWriteTracker.Instance;
    private readonly ConflictHandler? conflicts = conflicts;

    public const string CursorKey = "LastPulledSequence";
    private const int PullLimit = 500;

    public long GetCursor() => _state.GetSequence(CursorKey);

    public async Task<PullApplyOutcome> PullAndApplyAsync(long until, CancellationToken ct = default)
    {
        var cursor = GetCursor();
        while (true)
        {
            ct.ThrowIfCancellationRequested();
            var window = await FetchWindowAsync(cursor, until, ct);
            if (window is null)
                return PullApplyOutcome.TransientFailure;
            if (window is { Outcome: PullApplyOutcome.Applied })
            {
                foreach (var change in window.Changes)
                {
                    var applied = await ApplyChangeAsync(change, ct);
                    if (!applied)
                        return PullApplyOutcome.TransientFailure;
                }
                PersistCursor(window.NextCursor);
                cursor = window.NextCursor;
                if (!window.HasMore)
                    return PullApplyOutcome.Applied;
            }
            else
            {
                return window.Outcome;
            }
        }
    }

    private sealed record WindowFetch(IReadOnlyList<RemoteChange> Changes, long NextCursor, bool HasMore, PullApplyOutcome Outcome);

    private async Task<WindowFetch?> FetchWindowAsync(long since, long until, CancellationToken ct)
    {
        using var req = new HttpRequestMessage(HttpMethod.Get,
            $"api/sync/pull?since={since}&until={until}&limit={PullLimit}");
        using var res = await auth.SendWithAuthAsync(req, allowRefresh: true, ct);
        switch (res.StatusCode)
        {
            case HttpStatusCode.Unauthorized:
                return new WindowFetch(Array.Empty<RemoteChange>(), since, false, PullApplyOutcome.AuthFailed);
            case HttpStatusCode.Forbidden:
                return new WindowFetch(Array.Empty<RemoteChange>(), since, false, PullApplyOutcome.Forbidden);
            case HttpStatusCode.OK:
                break;
            default:
                return null;
        }
        using var doc = JsonDocument.Parse(await res.Content.ReadAsStringAsync(ct));
        var root = doc.RootElement;
        var changes = root.GetProperty("changes").EnumerateArray()
            .Select(c => new RemoteChange(
                c.GetProperty("sequence").GetInt64(),
                c.GetProperty("entityType").GetString()!,
                c.GetProperty("entityId").GetGuid(),
                c.GetProperty("operation").GetString()!,
                c.TryGetProperty("name", out var n) ? n.GetString() : null,
                c.TryGetProperty("parentFolderId", out var p) && p.ValueKind != JsonValueKind.Null ? p.GetGuid() : null,
                c.TryGetProperty("hash", out var h) ? h.GetString() : null,
                c.TryGetProperty("size", out var s) && s.ValueKind != JsonValueKind.Null ? s.GetInt64() : null,
                c.TryGetProperty("contentType", out var t) ? t.GetString() : null,
                c.TryGetProperty("originDeviceId", out var o) && o.ValueKind != JsonValueKind.Null ? o.GetGuid() : null))
            .ToList();
        return new WindowFetch(
            changes,
            root.GetProperty("nextCursor").GetInt64(),
            root.GetProperty("hasMore").GetBoolean(),
            PullApplyOutcome.Applied);
    }

    private async Task<bool> ApplyChangeAsync(RemoteChange change, CancellationToken ct)
    {
        try
        {
            if (IsOwnConfirmedChange(change))
                return true;
            return change.EntityType switch
            {
                "File" => await ApplyFileChangeAsync(change, ct),
                "Folder" => ApplyFolderChange(change),
                _ => false
            };
        }
        catch (IOException)
        {
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }
        catch (HttpRequestException)
        {
            return false;
        }
        catch (TaskCanceledException)
        {
            return false;
        }
    }

    private bool IsOwnConfirmedChange(RemoteChange change)
    {
        if (change.OriginDeviceId is null || auth.DeviceId is null) return false;
        if (!Guid.TryParse(auth.DeviceId, out var ownId) || ownId != change.OriginDeviceId) return false;
        if (change.EntityType == "File")
        {
            var row = _files.GetByServerId(change.EntityId);
            if (row is null) return false;
            if (change.Operation == "Deleted") return false;
            return change.Hash is null || string.Equals(row.Hash, change.Hash, StringComparison.OrdinalIgnoreCase);
        }
        else
        {
            var row = _folders.GetByServerId(change.EntityId);
            if (row is null) return false;
            if (change.Operation == "Deleted") return false;
            if (change.Name is null) return false;
            var expected = ResolveFolderPath(change.EntityId, change.ParentFolderId, change.Name);
            return expected is not null
                && string.Equals(row.RelativePath, expected, StringComparison.OrdinalIgnoreCase);
        }
    }

    private async Task<bool> ApplyFileChangeAsync(RemoteChange change, CancellationToken ct)
    {
        switch (change.Operation)
        {
            case "Created":
            case "Modified":
                return await DownloadAndReplaceAsync(change, ct);
            case "Renamed":
            case "Moved":
                return RelocateLocalRow(change);
            case "Deleted":
                return DeleteLocalFile(change);
            default:
                return false;
        }
    }

    private bool ApplyFolderChange(RemoteChange change)
    {
        switch (change.Operation)
        {
            case "Created":
                return CreateLocalFolder(change);
            case "Renamed":
            case "Moved":
                return RelocateLocalFolder(change);
            case "Deleted":
                return DeleteLocalFolder(change);
            default:
                return false;
        }
    }

    private async Task<bool> DownloadAndReplaceAsync(RemoteChange change, CancellationToken ct)
    {
        if (change.Name is null || change.Hash is null) return false;
        var relPath = ResolveFilePath(change.EntityId, change.ParentFolderId, change.Name);
        if (relPath is null) return false;
        var fullPath = ToFullPath(relPath);
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
        var tempPath = fullPath + "." + Guid.NewGuid().ToString("N") + ".nexsync-temp";
        try
        {
            using (var req = new HttpRequestMessage(HttpMethod.Get, $"api/files/{change.EntityId}/download"))
            using (var res = await auth.SendWithAuthAsync(req, allowRefresh: true, ct))
            {
                if (res.StatusCode != HttpStatusCode.OK) return false;
                using var net = await res.Content.ReadAsStreamAsync(ct);
                using var tmp = new FileStream(tempPath, FileMode.CreateNew, FileAccess.Write, FileShare.None);
                await net.CopyToAsync(tmp, ct);
            }
            string actualHash;
            using (var sha = SHA256.Create())
            using (var tmp = new FileStream(tempPath, FileMode.Open, FileAccess.Read, FileShare.Read))
                actualHash = Convert.ToHexString(sha.ComputeHash(tmp));
            if (!string.Equals(actualHash, change.Hash, StringComparison.OrdinalIgnoreCase))
                return false;
            using (_suppression.Suppress(relPath, change.Hash))
                File.Move(tempPath, fullPath, overwrite: true);
            tempPath = string.Empty;
            using var tx = db.BeginTransaction();
            _files.Upsert(new LocalFileRow(relPath, change.EntityId,
                ResolveParentServerIdForRow(change.ParentFolderId),
                change.Hash, change.Size, change.Sequence, DateTime.UtcNow), tx);
            tx.Commit();
            return true;
        }
        finally
        {
            if (!string.IsNullOrEmpty(tempPath) && File.Exists(tempPath))
            {
                try { File.Delete(tempPath); } catch (IOException) { }
            }
        }
    }

    private bool RelocateLocalRow(RemoteChange change)
    {
        if (change.Name is null) return false;
        var row = _files.GetByServerId(change.EntityId);
        if (row is null) return false;
        var relPath = ResolveFilePath(change.EntityId, change.ParentFolderId, change.Name);
        if (relPath is null) return false;
        if (!string.Equals(row.RelativePath, relPath, StringComparison.OrdinalIgnoreCase))
        {
            var oldFull = ToFullPath(row.RelativePath);
            var newFull = ToFullPath(relPath);
            if (File.Exists(oldFull))
            {
                Directory.CreateDirectory(Path.GetDirectoryName(newFull)!);
                using (_suppression.Suppress(row.RelativePath, row.Hash))
                using (_suppression.Suppress(relPath, row.Hash))
                    File.Move(oldFull, newFull, overwrite: true);
            }
        }
        using var tx = db.BeginTransaction();
        _files.Delete(row.RelativePath, tx);
        _files.Upsert(new LocalFileRow(relPath, row.ServerFileId, change.ParentFolderId,
            row.Hash, row.Size, change.Sequence, DateTime.UtcNow), tx);
        tx.Commit();
        return true;
    }

    private bool DeleteLocalFile(RemoteChange change)
    {
        var row = _files.GetByServerId(change.EntityId);
        if (row is not null)
        {
            if (conflicts is not null && conflicts.HasUnpushedChanges(row.RelativePath))
            {
                if (!conflicts.PreserveForRemoteDelete(row.RelativePath, out _))
                    return false;
                conflicts.CancelOpsUnder(row.RelativePath);
            }
            var fullPath = ToFullPath(row.RelativePath);
            if (File.Exists(fullPath))
            {
                using (_suppression.Suppress(row.RelativePath, row.Hash))
                    File.Delete(fullPath);
            }
            using var tx = db.BeginTransaction();
            _files.Delete(row.RelativePath, tx);
            tx.Commit();
        }
        return true;
    }

    private bool CreateLocalFolder(RemoteChange change)
    {
        if (change.Name is null) return false;
        var relPath = ResolveFolderPath(change.EntityId, change.ParentFolderId, change.Name);
        if (relPath is null) return false;
        Directory.CreateDirectory(ToFullPath(relPath));
        using var tx = db.BeginTransaction();
        _folders.Upsert(new LocalFolderRow(relPath, change.EntityId,
            ParentOf(relPath), change.Sequence, DateTime.UtcNow), tx);
        tx.Commit();
        return true;
    }

    private bool RelocateLocalFolder(RemoteChange change)
    {
        if (change.Name is null) return false;
        var row = _folders.GetByServerId(change.EntityId);
        if (row is null) return false;
        var relPath = ResolveFolderPath(change.EntityId, change.ParentFolderId, change.Name);
        if (relPath is null) return false;
        if (!string.Equals(row.RelativePath, relPath, StringComparison.OrdinalIgnoreCase))
        {
            var oldFull = ToFullPath(row.RelativePath);
            var newFull = ToFullPath(relPath);
            if (Directory.Exists(oldFull) && !Directory.Exists(newFull))
            {
                Directory.CreateDirectory(Path.GetDirectoryName(newFull)!);
                using (_suppression.Suppress(row.RelativePath, null))
                using (_suppression.Suppress(relPath, null))
                    Directory.Move(oldFull, newFull);
            }
            using var tx = db.BeginTransaction();
            RewriteDescendantPaths(row.RelativePath, relPath, change.Sequence, tx);
            _folders.Delete(row.RelativePath, tx);
            _folders.Upsert(new LocalFolderRow(relPath, row.ServerFolderId, ParentOf(relPath),
                change.Sequence, DateTime.UtcNow), tx);
            tx.Commit();
        }
        else
        {
            using var tx = db.BeginTransaction();
            _folders.Upsert(new LocalFolderRow(row.RelativePath, row.ServerFolderId, row.ParentRelativePath,
                change.Sequence, DateTime.UtcNow), tx);
            tx.Commit();
        }
        return true;
    }

    private void RewriteDescendantPaths(string oldPrefix, string newPrefix, long sequence, Microsoft.Data.Sqlite.SqliteTransaction tx)
    {
        foreach (var child in _folders.GetDescendants(oldPrefix, tx))
        {
            var suffix = child.RelativePath[oldPrefix.Length..].TrimStart('/');
            var childNew = newPrefix + "/" + suffix;
            _folders.Delete(child.RelativePath, tx);
            _folders.Upsert(new LocalFolderRow(childNew, child.ServerFolderId,
                ParentOf(childNew), sequence, DateTime.UtcNow), tx);
        }
        foreach (var file in _files.GetByFolderPath(oldPrefix, tx))
        {
            var suffix = file.RelativePath[oldPrefix.Length..].TrimStart('/');
            var fileNew = newPrefix + "/" + suffix;
            _files.Delete(file.RelativePath, tx);
            _files.Upsert(new LocalFileRow(fileNew, file.ServerFileId, file.ServerFolderId,
                file.Hash, file.Size, sequence, DateTime.UtcNow), tx);
        }
    }

    private bool DeleteLocalFolder(RemoteChange change)
    {
        var row = _folders.GetByServerId(change.EntityId);
        if (row is not null)
        {
            if (conflicts is not null && conflicts.HasUnpushedChanges(row.RelativePath))
            {
                if (!conflicts.PreserveForRemoteDelete(row.RelativePath, out _))
                    return false;
                conflicts.CancelOpsUnder(row.RelativePath);
            }
            var fullPath = ToFullPath(row.RelativePath);
            if (Directory.Exists(fullPath))
            {
                using (_suppression.Suppress(row.RelativePath, null))
                    Directory.Delete(fullPath, recursive: true);
            }
            using var tx = db.BeginTransaction();
            foreach (var child in _folders.GetDescendants(row.RelativePath, tx))
                _folders.Delete(child.RelativePath, tx);
            foreach (var file in _files.GetByFolderPath(row.RelativePath, tx))
                _files.Delete(file.RelativePath, tx);
            _folders.Delete(row.RelativePath, tx);
            tx.Commit();
        }
        return true;
    }

    private void PersistCursor(long nextCursor)
    {
        using var tx = db.BeginTransaction();
        _state.Set(PullPipeline.CursorKey, nextCursor.ToString(), tx);
        tx.Commit();
    }

    private string? ResolveFilePath(Guid entityId, Guid? parentFolderId, string name)
    {
        var parentPath = ResolveFolderLocalPath(parentFolderId);
        if (parentPath is null && parentFolderId.HasValue) return null;
        return parentPath is null ? name : parentPath + "/" + name;
    }

    private string? ResolveFolderPath(Guid entityId, Guid? parentFolderId, string name)
    {
        var parentPath = ResolveFolderLocalPath(parentFolderId);
        if (parentPath is null && parentFolderId.HasValue) return null;
        return parentPath is null ? name : parentPath + "/" + name;
    }

    private string? ResolveFolderLocalPath(Guid? parentFolderId)
    {
        if (!parentFolderId.HasValue) return null;
        return _folders.GetByServerId(parentFolderId.Value)?.RelativePath;
    }

    private Guid? ResolveParentServerIdForRow(Guid? parentFolderId) => parentFolderId;

    private string ToFullPath(string relPath) =>
        Path.Combine(localRoot, relPath.Replace('/', Path.DirectorySeparatorChar));

    private static string? ParentOf(string relPath)
    {
        var idx = relPath.LastIndexOf('/');
        return idx <= 0 ? null : relPath[..idx];
    }
}
