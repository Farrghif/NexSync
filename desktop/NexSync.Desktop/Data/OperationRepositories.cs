using System;
using System.Collections.Generic;
using Microsoft.Data.Sqlite;

namespace NexSync.Desktop.Data;

public sealed class PendingOperationRepository(SqliteConnection connection)
{
    public void Enqueue(PendingOperationRow op, SqliteTransaction? tx = null)
    {
        using var cmd = connection.CreateCommand();
        if (tx is not null) cmd.Transaction = tx;
        cmd.CommandText = """
            INSERT INTO PendingOperations (OperationId, OpType, RelPath, NewRelPath, ServerId, ParentServerId, RetryCount, Status, CreatedAt)
            VALUES ($id, $type, $path, $newPath, $serverId, $parentId, $retry, $status, $created);
            """;
        cmd.Parameters.AddWithValue("$id", op.OperationId.ToString());
        cmd.Parameters.AddWithValue("$type", op.OpType.ToString());
        cmd.Parameters.AddWithValue("$path", LocalPath.Canonicalize(op.RelPath));
        cmd.Parameters.AddWithValue("$newPath", op.NewRelPath is null ? DBNull.Value : LocalPath.Canonicalize(op.NewRelPath));
        cmd.Parameters.AddWithValue("$serverId", (object?)op.ServerId?.ToString() ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$parentId", (object?)op.ParentServerId?.ToString() ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$retry", op.RetryCount);
        cmd.Parameters.AddWithValue("$status", op.Status.ToString());
        cmd.Parameters.AddWithValue("$created", op.CreatedAt.ToString("O"));
        cmd.ExecuteNonQuery();
    }

    public List<PendingOperationRow> DequeueBatch(int maxCount, SqliteTransaction? tx = null)
    {
        using var cmd = connection.CreateCommand();
        if (tx is not null) cmd.Transaction = tx;
        cmd.CommandText = """
            SELECT OperationId, OpType, RelPath, NewRelPath, ServerId, ParentServerId, RetryCount, Status, CreatedAt
            FROM PendingOperations WHERE Status = 'Pending'
            ORDER BY CreatedAt, OperationId LIMIT $max;
            """;
        cmd.Parameters.AddWithValue("$max", maxCount);
        using var reader = cmd.ExecuteReader();
        var result = new List<PendingOperationRow>();
        while (reader.Read()) result.Add(Map(reader));
        return result;
    }

    public int CountPending(SqliteTransaction? tx = null)
    {
        using var cmd = connection.CreateCommand();
        if (tx is not null) cmd.Transaction = tx;
        cmd.CommandText = "SELECT COUNT(*) FROM PendingOperations WHERE Status = 'Pending';";
        return Convert.ToInt32(cmd.ExecuteScalar());
    }

    public List<PendingOperationRow> GetByPath(string relPath, SqliteTransaction? tx = null)
    {
        using var cmd = connection.CreateCommand();
        if (tx is not null) cmd.Transaction = tx;
        cmd.CommandText = """
            SELECT OperationId, OpType, RelPath, NewRelPath, ServerId, ParentServerId, RetryCount, Status, CreatedAt
            FROM PendingOperations WHERE RelPath = $path OR NewRelPath = $path ORDER BY CreatedAt, OperationId;
            """;
        cmd.Parameters.AddWithValue("$path", LocalPath.Canonicalize(relPath));
        using var reader = cmd.ExecuteReader();
        var result = new List<PendingOperationRow>();
        while (reader.Read()) result.Add(Map(reader));
        return result;
    }

    public List<PendingOperationRow> GetOpsWithDestinationUnder(string relPath, SqliteTransaction? tx = null)
    {
        var prefix = LocalPath.Canonicalize(relPath) + "/";
        using var cmd = connection.CreateCommand();
        if (tx is not null) cmd.Transaction = tx;
        cmd.CommandText = """
            SELECT OperationId, OpType, RelPath, NewRelPath, ServerId, ParentServerId, RetryCount, Status, CreatedAt
            FROM PendingOperations WHERE NewRelPath LIKE $prefix || '%' ESCAPE '\' ORDER BY CreatedAt, OperationId;
            """;
        cmd.Parameters.AddWithValue("$prefix", prefix.Replace(@"\", @"\\").Replace("%", @"\%").Replace("_", @"\_"));
        using var reader = cmd.ExecuteReader();
        var result = new List<PendingOperationRow>();
        while (reader.Read()) result.Add(Map(reader));
        return result;
    }

    public void DeleteByPath(string relPath, SqliteTransaction? tx = null)
    {
        using var cmd = connection.CreateCommand();
        if (tx is not null) cmd.Transaction = tx;
        cmd.CommandText = "DELETE FROM PendingOperations WHERE RelPath = $path OR NewRelPath = $path;";
        cmd.Parameters.AddWithValue("$path", LocalPath.Canonicalize(relPath));
        cmd.ExecuteNonQuery();
    }

    public void MarkDone(Guid operationId, SqliteTransaction? tx = null)
    {
        using var cmd = connection.CreateCommand();
        if (tx is not null) cmd.Transaction = tx;
        cmd.CommandText = "DELETE FROM PendingOperations WHERE OperationId = $id;";
        cmd.Parameters.AddWithValue("$id", operationId.ToString());
        cmd.ExecuteNonQuery();
    }

    public void MarkFailed(Guid operationId, SqliteTransaction? tx = null)
    {
        using var cmd = connection.CreateCommand();
        if (tx is not null) cmd.Transaction = tx;
        cmd.CommandText = "UPDATE PendingOperations SET Status = 'Failed', RetryCount = RetryCount + 1 WHERE OperationId = $id;";
        cmd.Parameters.AddWithValue("$id", operationId.ToString());
        cmd.ExecuteNonQuery();
    }

    public void RequeueFailedAsPending(SqliteTransaction? tx = null)
    {
        using var cmd = connection.CreateCommand();
        if (tx is not null) cmd.Transaction = tx;
        cmd.CommandText = "UPDATE PendingOperations SET Status = 'Pending' WHERE Status = 'Failed';";
        cmd.ExecuteNonQuery();
    }

    public void ClearAll(SqliteTransaction? tx = null)
    {
        using var cmd = connection.CreateCommand();
        if (tx is not null) cmd.Transaction = tx;
        cmd.CommandText = "DELETE FROM PendingOperations;";
        cmd.ExecuteNonQuery();
    }

    private static PendingOperationRow Map(SqliteDataReader r) => new(
        Guid.Parse(r.GetString(0)),
        Enum.Parse<PendingOpType>(r.GetString(1)),
        r.GetString(2),
        r.IsDBNull(3) ? null : r.GetString(3),
        r.IsDBNull(4) ? null : Guid.Parse(r.GetString(4)),
        r.IsDBNull(5) ? null : Guid.Parse(r.GetString(5)),
        r.GetInt32(6),
        Enum.Parse<PendingOpStatus>(r.GetString(7)),
        DateTime.Parse(r.GetString(8), null, System.Globalization.DateTimeStyles.RoundtripKind));
}

public sealed class ProcessedOperationRepository(SqliteConnection connection)
{
    public void Record(ProcessedOperationRow row, SqliteTransaction? tx = null)
    {
        using var cmd = connection.CreateCommand();
        if (tx is not null) cmd.Transaction = tx;
        cmd.CommandText = """
            INSERT INTO ProcessedOperations (OperationId, ResultSummary, CreatedAt)
            VALUES ($id, $summary, $created)
            ON CONFLICT(OperationId) DO NOTHING;
            """;
        cmd.Parameters.AddWithValue("$id", row.OperationId.ToString());
        cmd.Parameters.AddWithValue("$summary", row.ResultSummary);
        cmd.Parameters.AddWithValue("$created", row.CreatedAt.ToString("O"));
        cmd.ExecuteNonQuery();
    }

    public bool Contains(Guid operationId, SqliteTransaction? tx = null)
    {
        using var cmd = connection.CreateCommand();
        if (tx is not null) cmd.Transaction = tx;
        cmd.CommandText = "SELECT COUNT(*) FROM ProcessedOperations WHERE OperationId = $id;";
        cmd.Parameters.AddWithValue("$id", operationId.ToString());
        return Convert.ToInt32(cmd.ExecuteScalar()) > 0;
    }
}

public sealed class SyncStateRepository(SqliteConnection connection)
{
    public string? Get(string key, SqliteTransaction? tx = null)
    {
        using var cmd = connection.CreateCommand();
        if (tx is not null) cmd.Transaction = tx;
        cmd.CommandText = "SELECT Value FROM SyncState WHERE Key = $key;";
        cmd.Parameters.AddWithValue("$key", key);
        var result = cmd.ExecuteScalar();
        return result is null || result is DBNull ? null : (string)result;
    }

    public void Set(string key, string value, SqliteTransaction? tx = null)
    {
        using var cmd = connection.CreateCommand();
        if (tx is not null) cmd.Transaction = tx;
        cmd.CommandText = """
            INSERT INTO SyncState (Key, Value) VALUES ($key, $value)
            ON CONFLICT(Key) DO UPDATE SET Value = excluded.Value;
            """;
        cmd.Parameters.AddWithValue("$key", key);
        cmd.Parameters.AddWithValue("$value", value);
        cmd.ExecuteNonQuery();
    }

    public long GetSequence(string key, SqliteTransaction? tx = null)
        => long.TryParse(Get(key, tx), out var seq) ? seq : 0;
}
