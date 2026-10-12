using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using NexSync.Desktop.Data;

namespace NexSync.Desktop.Sync;

public sealed class ConflictHandler(
    LocalDb db,
    string localRoot,
    Func<string, Stream>? openRead = null)
{
    private readonly Func<string, Stream> _openRead =
        openRead ?? (path => new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite));
    public const string RecoveryDirName = ".nexsync";
    public const string ConflictsDir = "Recovery/Conflicts";
    public const string DeletedByServerDir = "Recovery/DeletedByServer";

    private readonly PendingOperationRepository _pending = new(db.Connection);
    private readonly LocalFileRepository _files = new(db.Connection);
    private readonly LocalFolderRepository _folders = new(db.Connection);

    public string? LastError { get; private set; }

    public string? PreservePushConflict(PendingOperationRow op)
    {
        LastError = null;
        var fullPath = ToFullPath(op.RelPath);
        var isDir = Directory.Exists(fullPath);
        var isFile = File.Exists(fullPath);
        if (!isDir && !isFile)
            return null;
        var fingerprint = isFile
            ? ComputeFileFingerprint(fullPath)
            : ComputeDirectoryFingerprint(fullPath);
        if (fingerprint is null)
        {
            LastError = $"Cannot read '{op.RelPath}' to compute preservation fingerprint.";
            return null;
        }
        var dest = Path.Combine(
            localRoot,
            RecoveryDirName,
            ConflictsDir,
            fingerprint,
            op.RelPath.Replace('/', Path.DirectorySeparatorChar));
        if (isFile)
        {
            if (IsAlreadyPreserved(dest, isDir: false))
                return dest;
        }
        else
        {
            if (IsAlreadyPreserved(dest, isDir: true))
                return dest;
        }
        var staging = dest + ".staging-" + Guid.NewGuid().ToString("N");
        try
        {
            if (isFile)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(staging)!);
                File.Copy(fullPath, staging, overwrite: false);
            }
            else
            {
                CopyDirectory(fullPath, staging);
            }
            Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
            if (isDir)
                Directory.Move(staging, dest);
            else
                File.Move(staging, dest);
        }
        catch (IOException ex)
        {
            CleanupStaging(staging);
            LastError = $"Failed to preserve '{op.RelPath}': {ex.Message}";
            return null;
        }
        catch (UnauthorizedAccessException ex)
        {
            CleanupStaging(staging);
            LastError = $"Access denied preserving '{op.RelPath}': {ex.Message}";
            return null;
        }
        return dest;
    }

    public bool PreserveForRemoteDelete(string relPath, out string? recoveryPath)
    {
        LastError = null;
        recoveryPath = null;
        var fullPath = ToFullPath(relPath);
        var isDir = Directory.Exists(fullPath);
        var isFile = File.Exists(fullPath);
        if (!isDir && !isFile)
            return true;
        var isEmptyDir = isDir && !Directory.EnumerateFileSystemEntries(fullPath).Any();
        var fingerprint = isEmptyDir
            ? "empty-dir"
            : isFile
                ? ComputeFileFingerprint(fullPath)
                : ComputeDirectoryFingerprint(fullPath);
        if (fingerprint is null)
        {
            LastError = $"Cannot read '{relPath}' to compute preservation fingerprint.";
            return false;
        }
        var dest = Path.Combine(
            localRoot,
            RecoveryDirName,
            DeletedByServerDir,
            fingerprint,
            relPath.Replace('/', Path.DirectorySeparatorChar));
        if (isFile)
        {
            if (IsAlreadyPreserved(dest, isDir: false))
            {
                recoveryPath = dest;
                return true;
            }
        }
        else
        {
            if (IsAlreadyPreserved(dest, isDir: true))
            {
                recoveryPath = dest;
                return true;
            }
        }
        var staging = dest + ".staging-" + Guid.NewGuid().ToString("N");
        try
        {
            if (isFile)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(staging)!);
                File.Copy(fullPath, staging, overwrite: false);
            }
            else
            {
                CopyDirectory(fullPath, staging);
            }
            Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
            if (isDir)
                Directory.Move(staging, dest);
            else
                File.Move(staging, dest);
        }
        catch (IOException ex)
        {
            CleanupStaging(staging);
            LastError = $"Failed to preserve '{relPath}' before remote delete: {ex.Message}";
            return false;
        }
        catch (UnauthorizedAccessException ex)
        {
            CleanupStaging(staging);
            LastError = $"Access denied preserving '{relPath}' before remote delete: {ex.Message}";
            return false;
        }
        recoveryPath = dest;
        return true;
    }

    public bool HasUnpushedChanges(string relPath)
    {
        using var tx = db.BeginTransaction();
        try
        {
            if (_pending.GetByPath(relPath, tx).Count > 0)
                return true;
            var file = _files.GetByPath(relPath, tx);
            if (file is not null)
                return DiskHashDiffers(ToFullPath(relPath), file.Hash);
            var folder = _folders.GetByPath(relPath, tx);
            if (folder is not null)
            {
                if (_pending.GetOpsWithDestinationUnder(relPath, tx).Count > 0)
                    return true;
                foreach (var child in _folders.GetDescendants(relPath, tx))
                {
                    if (_pending.GetByPath(child.RelativePath, tx).Count > 0)
                        return true;
                }
                foreach (var childFile in _files.GetByFolderPath(relPath, tx))
                {
                    if (_pending.GetByPath(childFile.RelativePath, tx).Count > 0)
                        return true;
                    if (DiskHashDiffers(ToFullPath(childFile.RelativePath), childFile.Hash))
                        return true;
                }
                if (HasUntrackedDiskContent(ToFullPath(relPath)))
                    return true;
            }
            tx.Commit();
            return false;
        }
        catch
        {
            tx.Rollback();
            throw;
        }
    }

    private bool HasUntrackedDiskContent(string fullFolderPath)
    {
        if (!Directory.Exists(fullFolderPath))
            return false;
        try
        {
            foreach (var file in Directory.EnumerateFiles(fullFolderPath, "*", SearchOption.AllDirectories))
            {
                var rel = Path.GetRelativePath(localRoot, file).Replace(Path.DirectorySeparatorChar, '/');
                string canonical;
                try
                {
                    canonical = LocalPath.Canonicalize(rel);
                }
                catch (ArgumentException)
                {
                    continue;
                }
                if (IsInternalRelative(canonical))
                    continue;
                if (_files.GetByPath(canonical) is null)
                    return true;
            }
            return false;
        }
        catch (IOException)
        {
            return true;
        }
        catch (UnauthorizedAccessException)
        {
            return true;
        }
    }

    private static bool IsInternalRelative(string canonicalRelPath) =>
        canonicalRelPath.Equals(".nexsync", StringComparison.OrdinalIgnoreCase)
        || canonicalRelPath.StartsWith(".nexsync/", StringComparison.OrdinalIgnoreCase)
        || Path.GetFileName(canonicalRelPath).EndsWith(".nexsync-temp", StringComparison.OrdinalIgnoreCase);

    public void CancelOpsUnder(string relPath)
    {
        using var tx = db.BeginTransaction();
        try
        {
            foreach (var op in _pending.GetOpsUnder(relPath, tx))
                _pending.MarkDone(op.OperationId, tx);
            tx.Commit();
        }
        catch
        {
            tx.Rollback();
            throw;
        }
    }

    public List<PendingOperationRow> GetOpsFor(string relPath) => _pending.GetByPath(relPath);

    private string ToFullPath(string relPath) =>
        Path.Combine(localRoot, relPath.Replace('/', Path.DirectorySeparatorChar));

    private string? ComputeFileFingerprint(string fullPath)
    {
        try
        {
            using var sha = SHA256.Create();
            using var stream = _openRead(fullPath);
            return Convert.ToHexString(sha.ComputeHash(stream));
        }
        catch (IOException)
        {
            return null;
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }
    }

    private string? ComputeDirectoryFingerprint(string fullPath)
    {
        try
        {
            var entries = new List<string>();
            foreach (var file in Directory.EnumerateFiles(fullPath, "*", SearchOption.AllDirectories).OrderBy(f => f, StringComparer.Ordinal))
            {
                var rel = Path.GetRelativePath(fullPath, file).Replace(Path.DirectorySeparatorChar, '/');
                if (IsInternalRelative(rel))
                    continue;
                var hash = ComputeFileFingerprint(file);
                if (hash is null)
                    return null;
                entries.Add(rel + ":" + hash);
            }
            using var sha = SHA256.Create();
            return Convert.ToHexString(sha.ComputeHash(System.Text.Encoding.UTF8.GetBytes(string.Join("\n", entries))));
        }
        catch (IOException)
        {
            return null;
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }
    }

    private static bool IsAlreadyPreserved(string dest, bool isDir)
    {
        if (isDir)
            return Directory.Exists(dest);
        return File.Exists(dest);
    }

    private static void CleanupStaging(string staging)
    {
        try
        {
            if (File.Exists(staging))
                File.Delete(staging);
            else if (Directory.Exists(staging))
                Directory.Delete(staging, recursive: true);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    private bool DiskHashDiffers(string fullPath, string? recordedHash)
    {
        if (!File.Exists(fullPath))
            return recordedHash is not null;
        if (recordedHash is null)
            return true;
        try
        {
            using var sha = SHA256.Create();
            using var stream = _openRead(fullPath);
            return !string.Equals(Convert.ToHexString(sha.ComputeHash(stream)), recordedHash, StringComparison.OrdinalIgnoreCase);
        }
        catch (IOException)
        {
            return true;
        }
        catch (UnauthorizedAccessException)
        {
            return true;
        }
    }

    private static void CopyDirectory(string source, string dest)
    {
        Directory.CreateDirectory(dest);
        foreach (var file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
        {
            var target = Path.Combine(dest, Path.GetRelativePath(source, file));
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(file, target, overwrite: false);
        }
    }
}
