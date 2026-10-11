using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Data.Sqlite;
using NexSync.Desktop.Data;

namespace NexSync.Desktop.Sync;

public sealed class PendingQueueService(LocalDb db)
{
    private readonly PendingOperationRepository _pending = new(db.Connection);
    private readonly LocalFolderRepository _folders = new(db.Connection);
    private readonly LocalFileRepository _files = new(db.Connection);

    public Guid Enqueue(PendingOpType opType, string relPath, string? newRelPath = null, Guid? serverId = null, Guid? parentServerId = null)
    {
        var operationId = Guid.NewGuid();
        using var tx = db.BeginTransaction();
        var canonical = LocalPath.Canonicalize(relPath);
        var canonicalNew = newRelPath is null ? null : LocalPath.Canonicalize(newRelPath);
        if (IsDuplicatePending(opType, canonical, canonicalNew, tx))
            return ExistingDuplicateId(opType, canonical, canonicalNew, tx);
        _pending.Enqueue(new PendingOperationRow(
            operationId, opType, canonical, canonicalNew, serverId, parentServerId,
            0, PendingOpStatus.Pending, DateTime.UtcNow), tx);
        tx.Commit();
        return operationId;
    }

    public IReadOnlyList<PendingOperationRow> DequeueEligibleBatch(int maxCount)
    {
        if (maxCount <= 0) return Array.Empty<PendingOperationRow>();
        var all = _pending.DequeueBatch(int.MaxValue);
        var selected = new List<PendingOperationRow>();
        var selectedCreates = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var progressed = true;
        while (selected.Count < maxCount && progressed)
        {
            progressed = false;
            foreach (var op in all)
            {
                if (selected.Count >= maxCount) break;
                if (selected.Contains(op)) continue;
                if (!IsEligible(op, selectedCreates)) continue;
                selected.Add(op);
                progressed = true;
                if (IsCreateFolderOp(op))
                    selectedCreates.Add(op.RelPath);
            }
        }
        return selected;
    }

    public bool TryDropRedundantDelete(PendingOperationRow op)
    {
        if (op.OpType != PendingOpType.DeleteFile && op.OpType != PendingOpType.DeleteFolder)
            return false;
        var known = op.OpType == PendingOpType.DeleteFile
            ? _files.GetByPath(op.RelPath)?.ServerFileId.HasValue == true
            : _folders.GetByPath(op.RelPath)?.ServerFolderId.HasValue == true;
        if (known || op.ServerId.HasValue) return false;
        using var tx = db.BeginTransaction();
        _pending.MarkDone(op.OperationId, tx);
        tx.Commit();
        return true;
    }

    public void Complete(PendingOperationRow op, bool success)
    {
        using var tx = db.BeginTransaction();
        Complete(op, success, tx);
        tx.Commit();
    }

    public void Complete(PendingOperationRow op, bool success, SqliteTransaction tx)
    {
        if (success)
            _pending.MarkDone(op.OperationId, tx);
        else
            _pending.MarkFailed(op.OperationId, tx);
    }

    public int CountPending() => _pending.CountPending();

    public void RequeueFailed()
    {
        using var tx = db.BeginTransaction();
        _pending.RequeueFailedAsPending(tx);
        tx.Commit();
    }

    private bool IsEligible(PendingOperationRow op, HashSet<string> selectedCreates)
    {
        if (op.OpType is not (PendingOpType.CreateFile or PendingOpType.CreateFolder))
            return true;
        foreach (var ancestor in AncestorsOf(op.RelPath))
        {
            if (selectedCreates.Contains(ancestor)) continue;
            var folder = _folders.GetByPath(ancestor);
            if (folder?.ServerFolderId.HasValue == true) continue;
            if (folder is not null || HasPendingCreateFor(ancestor)) return false;
        }
        return true;
    }

    private bool HasPendingCreateFor(string ancestorPath)
    {
        using var cmd = db.Connection.CreateCommand();
        cmd.CommandText = """
            SELECT COUNT(*) FROM PendingOperations
            WHERE Status = 'Pending' AND OpType = 'CreateFolder' AND RelPath = $path;
            """;
        cmd.Parameters.AddWithValue("$path", ancestorPath);
        return Convert.ToInt32(cmd.ExecuteScalar()) > 0;
    }

    private static bool IsCreateFolderOp(PendingOperationRow op)
        => op.OpType == PendingOpType.CreateFolder;

    private static IEnumerable<string> AncestorsOf(string relPath)
    {
        var parts = LocalPath.Canonicalize(relPath).Split('/');
        for (var i = 1; i < parts.Length; i++)
            yield return string.Join('/', parts[..i]);
    }

    private bool IsDuplicatePending(PendingOpType opType, string relPath, string? newRelPath, SqliteTransaction tx)
    {
        using var cmd = db.Connection.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = """
            SELECT COUNT(*) FROM PendingOperations
            WHERE Status = 'Pending' AND OpType = $type AND RelPath = $path
              AND ((NewRelPath IS NULL AND $new IS NULL) OR NewRelPath = $new);
            """;
        cmd.Parameters.AddWithValue("$type", opType.ToString());
        cmd.Parameters.AddWithValue("$path", relPath);
        cmd.Parameters.AddWithValue("$new", (object?)newRelPath ?? DBNull.Value);
        return Convert.ToInt32(cmd.ExecuteScalar()) > 0;
    }

    private Guid ExistingDuplicateId(PendingOpType opType, string relPath, string? newRelPath, SqliteTransaction tx)
    {
        using var cmd = db.Connection.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = """
            SELECT OperationId FROM PendingOperations
            WHERE Status = 'Pending' AND OpType = $type AND RelPath = $path
              AND ((NewRelPath IS NULL AND $new IS NULL) OR NewRelPath = $new)
            ORDER BY CreatedAt, OperationId LIMIT 1;
            """;
        cmd.Parameters.AddWithValue("$type", opType.ToString());
        cmd.Parameters.AddWithValue("$path", relPath);
        cmd.Parameters.AddWithValue("$new", (object?)newRelPath ?? DBNull.Value);
        return Guid.Parse((string)cmd.ExecuteScalar()!);
    }
}
