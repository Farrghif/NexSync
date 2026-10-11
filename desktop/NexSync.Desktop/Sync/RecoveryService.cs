using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using NexSync.Desktop.Auth;
using NexSync.Desktop.Data;

namespace NexSync.Desktop.Sync;

public enum ReconcileOutcome
{
    Converged,
    AuthFailed,
    Forbidden,
    TransientFailure
}

public sealed class RecoveryService(
    LocalDb db,
    PendingQueueService queue,
    DesktopAuthService auth,
    string localRoot)
{
    private readonly LocalFolderRepository _folders = new(db.Connection);
    private readonly LocalFileRepository _files = new(db.Connection);
    private readonly PendingOperationRepository _pending = new(db.Connection);
    private readonly SyncStateRepository _state = new(db.Connection);

    public async Task<ReconcileOutcome> ReconcileAsync(CancellationToken ct = default)
        => await ReconcileAsync(RecoveryTestHooks.None, ct);

    public async Task<ReconcileOutcome> ReconcileAsync(RecoveryTestHooks hooks, CancellationToken ct = default)
    {
        var manifest = await FetchManifestAsync(ct);
        if (manifest is null)
            return ReconcileOutcome.TransientFailure;
        if (manifest.Auth == ReconcileOutcome.AuthFailed || manifest.Auth == ReconcileOutcome.Forbidden)
            return manifest.Auth;

        using var tx = db.BeginTransaction();
        try
        {
            ReconcileFolders(manifest.Folders, tx);
            if (hooks == RecoveryTestHooks.FailAfterManifest)
                throw new InvalidOperationException("Injected mid-transaction failure.");
            ReconcileFiles(manifest.Files, tx);
            var fresh = ScanDisk();
            CleanupStaleRows(fresh, manifest, tx);
            _pending.ClearAll(tx);
            RegenerateOpsFromDisk(fresh, manifest, tx);
            _state.Set(PullPipeline.CursorKey, manifest.SnapshotCursor.ToString(), tx);
            tx.Commit();
        }
        catch
        {
            tx.Rollback();
            throw;
        }
        return ReconcileOutcome.Converged;
    }

    public enum RecoveryTestHooks
    {
        None,
        FailAfterManifest
    }

    private sealed record ManifestEntry(
        long SnapshotCursor,
        List<ManifestFolder> Folders,
        List<ManifestFile> Files,
        ReconcileOutcome Auth);

    private sealed record ManifestFolder(Guid Id, string Name, Guid? ParentFolderId);
    private sealed record ManifestFile(Guid Id, string Name, Guid? FolderId, string Hash, long Size, string ContentType);

    private async Task<ManifestEntry?> FetchManifestAsync(CancellationToken ct)
    {
        HttpResponseMessage res;
        try
        {
            using var req = new HttpRequestMessage(HttpMethod.Get, "api/sync/manifest");
            res = await auth.SendWithAuthAsync(req, allowRefresh: true, ct);
        }
        catch (HttpRequestException)
        {
            return null;
        }
        catch (TaskCanceledException)
        {
            return null;
        }
        using (res)
        {
            if (res.StatusCode == HttpStatusCode.Unauthorized)
                return new ManifestEntry(0, new(), new(), ReconcileOutcome.AuthFailed);
            if (res.StatusCode == HttpStatusCode.Forbidden)
                return new ManifestEntry(0, new(), new(), ReconcileOutcome.Forbidden);
            if (res.StatusCode != HttpStatusCode.OK)
                return null;
            using var doc = JsonDocument.Parse(await res.Content.ReadAsStringAsync(ct));
            var root = doc.RootElement;
            var folders = root.GetProperty("folders").EnumerateArray()
                .Select(f => new ManifestFolder(
                    f.GetProperty("id").GetGuid(),
                    f.GetProperty("name").GetString()!,
                    f.TryGetProperty("parentFolderId", out var p) && p.ValueKind != JsonValueKind.Null ? p.GetGuid() : null))
                .ToList();
            var files = root.GetProperty("files").EnumerateArray()
                .Select(f => new ManifestFile(
                    f.GetProperty("id").GetGuid(),
                    f.GetProperty("name").GetString()!,
                    f.TryGetProperty("folderId", out var p) && p.ValueKind != JsonValueKind.Null ? p.GetGuid() : null,
                    f.GetProperty("hash").GetString()!,
                    f.GetProperty("size").GetInt64(),
                    f.GetProperty("contentType").GetString()!))
                .ToList();
            return new ManifestEntry(root.GetProperty("snapshotCursor").GetInt64(), folders, files, ReconcileOutcome.Converged);
        }
    }

    private sealed record DiskSnapshot(HashSet<string> Directories, Dictionary<string, DiskFile> Files);
    private sealed record DiskFile(long Size, string Hash);

    private DiskSnapshot ScanDisk()
    {
        var dirs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var files = new Dictionary<string, DiskFile>(StringComparer.OrdinalIgnoreCase);
        var root = Path.GetFullPath(localRoot);
        foreach (var dir in Directory.EnumerateDirectories(root, "*", SearchOption.AllDirectories))
        {
            var rel = ToRel(root, dir);
            if (rel is null || IsInternal(rel)) continue;
            dirs.Add(rel);
        }
        foreach (var file in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories))
        {
            var rel = ToRel(root, file);
            if (rel is null || IsInternal(rel)) continue;
            try
            {
                using var sha = SHA256.Create();
                using var stream = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                var size = stream.Length;
                var hash = Convert.ToHexString(sha.ComputeHash(stream));
                files[rel] = new DiskFile(size, hash);
            }
            catch (IOException)
            {
                continue;
            }
            catch (UnauthorizedAccessException)
            {
                continue;
            }
        }
        return new DiskSnapshot(dirs, files);
    }

    private static string? ToRel(string root, string fullPath)
    {
        string rel;
        try
        {
            rel = LocalPath.Canonicalize(Path.GetRelativePath(root, fullPath).Replace('\\', '/'));
        }
        catch (ArgumentException)
        {
            return null;
        }
        if (rel.StartsWith("..", StringComparison.Ordinal)) return null;
        return rel;
    }

    private static bool IsInternal(string relPath) =>
        relPath.Equals(FolderWatcherService.InternalDirName, StringComparison.OrdinalIgnoreCase)
        || relPath.StartsWith(FolderWatcherService.InternalDirName + "/", StringComparison.OrdinalIgnoreCase)
        || Path.GetFileName(relPath).EndsWith(FolderWatcherService.TempSuffix, StringComparison.OrdinalIgnoreCase);

    private void ReconcileFolders(
        List<ManifestFolder> server,
        Microsoft.Data.Sqlite.SqliteTransaction tx)
    {
        var byId = server.ToDictionary(f => f.Id);
        foreach (var folder in server)
        {
            var path = ResolveServerFolderPath(folder, byId);
            if (path is null) continue;
            var stale = _folders.GetAll(tx).FirstOrDefault(r =>
                r.ServerFolderId == folder.Id && !string.Equals(r.RelativePath, path, StringComparison.OrdinalIgnoreCase));
            if (stale is not null)
            {
                var moved = MoveDirectoryIfPossible(stale.RelativePath, path);
                if (moved || !Directory.Exists(Path.Combine(localRoot, stale.RelativePath.Replace('/', Path.DirectorySeparatorChar))))
                    _folders.Delete(stale.RelativePath, tx);
            }
            _folders.Upsert(new LocalFolderRow(path, folder.Id, ParentOf(path),
                0, DateTime.UtcNow), tx);
            try
            {
                var full = Path.Combine(localRoot, path.Replace('/', Path.DirectorySeparatorChar));
                if (!Directory.Exists(full))
                    Directory.CreateDirectory(full);
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
        var manifestIds = new HashSet<Guid>(server.Select(f => f.Id));
        foreach (var row in _folders.GetAll(tx))
        {
            if (row.ServerFolderId.HasValue && !manifestIds.Contains(row.ServerFolderId.Value))
                _folders.Upsert(new LocalFolderRow(row.RelativePath, null, row.ParentRelativePath, row.LastAppliedSequence, DateTime.UtcNow), tx);
        }
    }

    private bool MoveDirectoryIfPossible(string oldRelPath, string newRelPath)
    {
        try
        {
            var staleFull = Path.Combine(localRoot, oldRelPath.Replace('/', Path.DirectorySeparatorChar));
            var newFull = Path.Combine(localRoot, newRelPath.Replace('/', Path.DirectorySeparatorChar));
            if (string.Equals(staleFull, newFull, StringComparison.OrdinalIgnoreCase))
                return true;
            if (Directory.Exists(staleFull) && !Directory.Exists(newFull))
            {
                Directory.CreateDirectory(Path.GetDirectoryName(newFull)!);
                Directory.Move(staleFull, newFull);
            }
            return !Directory.Exists(staleFull) || Directory.Exists(newFull);
        }
        catch (IOException) { return false; }
        catch (UnauthorizedAccessException) { return false; }
    }

    private bool MoveFileIfPossible(string oldRelPath, string newRelPath)
    {
        try
        {
            var staleFull = Path.Combine(localRoot, oldRelPath.Replace('/', Path.DirectorySeparatorChar));
            var newFull = Path.Combine(localRoot, newRelPath.Replace('/', Path.DirectorySeparatorChar));
            if (string.Equals(staleFull, newFull, StringComparison.OrdinalIgnoreCase))
                return true;
            if (File.Exists(staleFull) && !File.Exists(newFull))
            {
                Directory.CreateDirectory(Path.GetDirectoryName(newFull)!);
                File.Move(staleFull, newFull);
            }
            return !File.Exists(staleFull) || File.Exists(newFull);
        }
        catch (IOException) { return false; }
        catch (UnauthorizedAccessException) { return false; }
    }

    private static string? ParentOf(string relPath)
    {
        var idx = relPath.LastIndexOf('/');
        return idx <= 0 ? null : relPath[..idx];
    }

    private string? ResolveServerFolderPath(ManifestFolder folder, Dictionary<Guid, ManifestFolder> byId)
    {
        var segments = new Stack<string>();
        var current = folder;
        var guard = 0;
        while (current is not null && guard++ < 256)
        {
            segments.Push(current.Name);
            if (!current.ParentFolderId.HasValue) break;
            byId.TryGetValue(current.ParentFolderId.Value, out current);
        }
        if (current is not null && current.ParentFolderId.HasValue) return null;
        try
        {
            return LocalPath.Canonicalize(string.Join('/', segments));
        }
        catch (ArgumentException)
        {
            return null;
        }
    }

    private void ReconcileFiles(
        List<ManifestFile> server,
        Microsoft.Data.Sqlite.SqliteTransaction tx)
    {
        var folderById = _folders.GetAll(tx)
            .Where(f => f.ServerFolderId.HasValue)
            .ToDictionary(f => f.ServerFolderId!.Value, f => f.RelativePath);
        var manifestIds = new HashSet<Guid>(server.Select(f => f.Id));

        foreach (var file in server)
        {
            string? path = null;
            if (file.FolderId.HasValue)
            {
                if (!folderById.TryGetValue(file.FolderId.Value, out var parent)) continue;
                path = parent + "/" + file.Name;
            }
            else
            {
                path = file.Name;
            }
            try { path = LocalPath.Canonicalize(path); }
            catch (ArgumentException) { continue; }
            var stale = _files.GetAll(tx).FirstOrDefault(r =>
                r.ServerFileId == file.Id && !string.Equals(r.RelativePath, path, StringComparison.OrdinalIgnoreCase));
            if (stale is not null)
            {
                if (MoveFileIfPossible(stale.RelativePath, path))
                    _files.Delete(stale.RelativePath, tx);
            }
            _files.Upsert(new LocalFileRow(path, file.Id, file.FolderId, file.Hash, file.Size, 0, DateTime.UtcNow), tx);
        }
        foreach (var row in _files.GetAll(tx))
        {
            if (row.ServerFileId.HasValue && !manifestIds.Contains(row.ServerFileId.Value))
                _files.Upsert(new LocalFileRow(row.RelativePath, null, null, row.Hash, row.Size, row.LastAppliedSequence, DateTime.UtcNow), tx);
        }
    }

    private void CleanupStaleRows(
        DiskSnapshot disk,
        ManifestEntry manifest,
        Microsoft.Data.Sqlite.SqliteTransaction tx)
    {
        var manifestFolderIds = new HashSet<Guid>(manifest.Folders.Select(f => f.Id));
        foreach (var row in _folders.GetAll(tx))
        {
            var onDisk = disk.Directories.Contains(row.RelativePath);
            var onServer = row.ServerFolderId.HasValue && manifestFolderIds.Contains(row.ServerFolderId.Value);
            if (!onDisk && !onServer)
                _folders.Delete(row.RelativePath, tx);
        }
        var manifestFileIds = new HashSet<Guid>(manifest.Files.Select(f => f.Id));
        foreach (var row in _files.GetAll(tx))
        {
            var onDisk = disk.Files.ContainsKey(row.RelativePath);
            var onServer = row.ServerFileId.HasValue && manifestFileIds.Contains(row.ServerFileId.Value);
            if (!onDisk && !onServer)
                _files.Delete(row.RelativePath, tx);
        }
    }

    private void RegenerateOpsFromDisk(
        DiskSnapshot disk,
        ManifestEntry manifest,
        Microsoft.Data.Sqlite.SqliteTransaction tx)
    {
        var serverHashes = manifest.Files
            .GroupBy(f => f.Hash, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

        foreach (var dir in disk.Directories.OrderBy(d => d, StringComparer.Ordinal))
        {
            var row = _folders.GetByPath(dir, tx);
            if (row is null)
                _folders.Upsert(new LocalFolderRow(dir, null, ParentOf(dir), 0, DateTime.UtcNow), tx);
            else if (row.ServerFolderId is null)
                _pending.Enqueue(new PendingOperationRow(Guid.NewGuid(), PendingOpType.CreateFolder,
                    dir, null, null, null, 0, PendingOpStatus.Pending, DateTime.UtcNow), tx);
        }
        foreach (var (path, file) in disk.Files.OrderBy(kv => kv.Key, StringComparer.Ordinal))
        {
            var row = _files.GetByPath(path, tx);
            if (row is null)
            {
                if (serverHashes.TryGetValue(file.Hash, out var match)
                    && _files.GetAll(tx).All(r => r.ServerFileId != match.Id)
                    && !_files.GetAll(tx).Any(r => string.Equals(r.RelativePath, path, StringComparison.OrdinalIgnoreCase)))
                {
                    _files.Upsert(new LocalFileRow(path, match.Id, match.FolderId, file.Hash, file.Size, manifest.SnapshotCursor, DateTime.UtcNow), tx);
                    continue;
                }
                _files.Upsert(new LocalFileRow(path, null, null, file.Hash, file.Size, 0, DateTime.UtcNow), tx);
                _pending.Enqueue(new PendingOperationRow(Guid.NewGuid(), PendingOpType.CreateFile,
                    path, null, null, null, 0, PendingOpStatus.Pending, DateTime.UtcNow), tx);
                continue;
            }
            if (row.ServerFileId.HasValue)
            {
                if (!string.Equals(row.Hash, file.Hash, StringComparison.OrdinalIgnoreCase))
                    _pending.Enqueue(new PendingOperationRow(Guid.NewGuid(), PendingOpType.ModifyFile,
                        path, null, row.ServerFileId, null, 0, PendingOpStatus.Pending, DateTime.UtcNow), tx);
                continue;
            }
            _pending.Enqueue(new PendingOperationRow(Guid.NewGuid(), PendingOpType.CreateFile,
                path, null, null, null, 0, PendingOpStatus.Pending, DateTime.UtcNow), tx);
        }
        var manifestIds = new HashSet<Guid>(manifest.Files.Select(f => f.Id));
        foreach (var row in _files.GetAll(tx))
        {
            if (row.ServerFileId.HasValue
                && manifestIds.Contains(row.ServerFileId.Value)
                && !disk.Files.ContainsKey(row.RelativePath))
                _pending.Enqueue(new PendingOperationRow(Guid.NewGuid(), PendingOpType.DeleteFile,
                    row.RelativePath, null, row.ServerFileId, null, 0, PendingOpStatus.Pending, DateTime.UtcNow), tx);
        }
        var manifestFolderIds = new HashSet<Guid>(manifest.Folders.Select(f => f.Id));
        foreach (var row in _folders.GetAll(tx))
        {
            if (row.ServerFolderId.HasValue
                && manifestFolderIds.Contains(row.ServerFolderId.Value)
                && !disk.Directories.Contains(row.RelativePath))
                _pending.Enqueue(new PendingOperationRow(Guid.NewGuid(), PendingOpType.DeleteFolder,
                    row.RelativePath, null, row.ServerFolderId, null, 0, PendingOpStatus.Pending, DateTime.UtcNow), tx);
        }
    }
}
