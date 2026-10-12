using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using NexSync.Desktop.Data;

namespace NexSync.Desktop.Sync;

public enum SetupChoice
{
    Empty,
    Import
}

public sealed record SetupInspection(bool IsEmpty, IReadOnlyList<string> ExistingRelativePaths);

public sealed class SetupService : IDisposable
{
    private readonly LocalDb? _ownedDb;
    private bool _disposed;

    public SetupService(
        LocalDb db,
        PendingQueueService queue,
        string localRoot,
        Func<string, Stream>? openRead = null,
        bool ownsDb = false)
    {
        _ownedDb = ownsDb ? db : null;
        Queue = queue;
        LocalRoot = localRoot;
        _openRead = openRead ?? (path => new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite));
        _folders = new LocalFolderRepository(db.Connection);
        _files = new LocalFileRepository(db.Connection);
        _state = new SyncStateRepository(db.Connection);
    }

    public static SetupService ForLocalRoot(string localRoot)
    {
        var dbPath = Path.Combine(Path.GetFullPath(localRoot), ".nexsync", "local.db");
        var db = new LocalDb(dbPath);
        return new SetupService(db, new PendingQueueService(db), localRoot, ownsDb: true);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _ownedDb?.Dispose();
    }

    private readonly LocalFolderRepository _folders;
    private readonly LocalFileRepository _files;
    private readonly SyncStateRepository _state;
    private readonly Func<string, Stream> _openRead;
    private PendingQueueService Queue { get; }
    private string LocalRoot { get; }

    public const string SetupChoiceKey = "SetupChoice";

    private sealed record ImportFile(string RelativePath, long Size, string Hash);

    public SetupInspection Inspect()
    {
        var existing = new List<string>();
        var root = Path.GetFullPath(LocalRoot);
        if (Directory.Exists(root))
        {
            foreach (var entry in EnumerateUserEntries(root))
            {
                var rel = ToRel(root, entry);
                if (rel is null) continue;
                existing.Add(rel);
            }
        }
        existing.Sort(StringComparer.Ordinal);
        return new SetupInspection(existing.Count == 0, existing);
    }

    public void ApplyEmptySetup()
    {
        Directory.CreateDirectory(LocalRoot);
        if (!Inspect().IsEmpty)
            throw new InvalidOperationException(
                "This folder already contains files. Pick an empty folder or choose 'Import existing files'.");
        _state.Set(SetupChoiceKey, SetupChoice.Empty.ToString());
    }

    public void ApplyImportSetup()
    {
        Directory.CreateDirectory(LocalRoot);
        var root = Path.GetFullPath(LocalRoot);
        var (dirs, files) = ScanForImport(root);
        foreach (var dir in dirs)
        {
            if (_folders.GetByPath(dir)?.ServerFolderId.HasValue == true) continue;
            _folders.Upsert(new LocalFolderRow(dir, null, ParentOf(dir), 0, DateTime.UtcNow));
            Queue.Enqueue(PendingOpType.CreateFolder, dir);
        }
        foreach (var file in files)
        {
            if (_files.GetByPath(file.RelativePath)?.ServerFileId.HasValue == true) continue;
            _files.Upsert(new LocalFileRow(file.RelativePath, null, null, file.Hash, file.Size, 0, DateTime.UtcNow));
            Queue.Enqueue(PendingOpType.CreateFile, file.RelativePath);
        }
        _state.Set(SetupChoiceKey, SetupChoice.Import.ToString());
    }

    private (List<string> Dirs, List<ImportFile> Files) ScanForImport(string root)
    {
        var dirs = new List<string>();
        var files = new List<ImportFile>();
        var unreadable = new List<string>();
        foreach (var entry in EnumerateUserEntries(root))
        {
            var rel = ToRel(root, entry);
            if (rel is null) continue;
            if (Directory.Exists(entry))
            {
                dirs.Add(rel);
                continue;
            }
            if (!File.Exists(entry)) continue;
            try
            {
                using var stream = _openRead(entry);
                using var sha = SHA256.Create();
                files.Add(new ImportFile(rel, stream.Length, Convert.ToHexString(sha.ComputeHash(stream))));
            }
            catch (IOException)
            {
                unreadable.Add(rel);
            }
            catch (UnauthorizedAccessException)
            {
                unreadable.Add(rel);
            }
        }
        if (unreadable.Count > 0)
            throw new InvalidOperationException(
                "Cannot read '" + string.Join(", ", unreadable) + "'. Resolve access and retry import; nothing was queued.");
        dirs.Sort(StringComparer.Ordinal);
        files.Sort((a, b) => StringComparer.Ordinal.Compare(a.RelativePath, b.RelativePath));
        return (dirs, files);
    }

    private static List<string> EnumerateUserEntries(string root)
    {
        List<string> entries;
        try
        {
            entries = Directory.EnumerateFileSystemEntries(root, "*", SearchOption.AllDirectories).ToList();
        }
        catch (IOException ex)
        {
            throw new InvalidOperationException($"Cannot scan local folder: {ex.Message}", ex);
        }
        catch (UnauthorizedAccessException ex)
        {
            throw new InvalidOperationException($"Cannot scan local folder: {ex.Message}", ex);
        }
        return entries.Where(e => !FolderWatcherService.IsInternalPath(root, e)).ToList();
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

    private static string? ParentOf(string relPath)
    {
        var idx = relPath.LastIndexOf('/');
        return idx <= 0 ? null : relPath[..idx];
    }
}
