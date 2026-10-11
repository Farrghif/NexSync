using System;
using System.Collections.Generic;
using Microsoft.Data.Sqlite;

namespace NexSync.Desktop.Data;

public sealed class LocalFolderRepository(SqliteConnection connection)
{
    public void Upsert(LocalFolderRow row, SqliteTransaction? tx = null)
    {
        using var cmd = connection.CreateCommand();
        if (tx is not null) cmd.Transaction = tx;
        cmd.CommandText = """
            INSERT INTO LocalFolders (RelativePath, ServerFolderId, ParentRelativePath, LastAppliedSequence, UpdatedAt)
            VALUES ($path, $serverId, $parent, $seq, $updated)
            ON CONFLICT(RelativePath) DO UPDATE SET
                ServerFolderId = excluded.ServerFolderId,
                ParentRelativePath = excluded.ParentRelativePath,
                LastAppliedSequence = excluded.LastAppliedSequence,
                UpdatedAt = excluded.UpdatedAt;
            """;
        cmd.Parameters.AddWithValue("$path", LocalPath.Canonicalize(row.RelativePath));
        cmd.Parameters.AddWithValue("$serverId", (object?)row.ServerFolderId?.ToString() ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$parent", row.ParentRelativePath is null ? DBNull.Value : LocalPath.Canonicalize(row.ParentRelativePath));
        cmd.Parameters.AddWithValue("$seq", row.LastAppliedSequence);
        cmd.Parameters.AddWithValue("$updated", row.UpdatedAt.ToString("O"));
        cmd.ExecuteNonQuery();
    }

    public LocalFolderRow? GetByPath(string relativePath, SqliteTransaction? tx = null)
    {
        using var cmd = connection.CreateCommand();
        if (tx is not null) cmd.Transaction = tx;
        cmd.CommandText = "SELECT RelativePath, ServerFolderId, ParentRelativePath, LastAppliedSequence, UpdatedAt FROM LocalFolders WHERE RelativePath = $path;";
        cmd.Parameters.AddWithValue("$path", LocalPath.Canonicalize(relativePath));
        using var reader = cmd.ExecuteReader();
        if (!reader.Read()) return null;
        return Map(reader);
    }

    public LocalFolderRow? GetByServerId(Guid serverId, SqliteTransaction? tx = null)
    {
        using var cmd = connection.CreateCommand();
        if (tx is not null) cmd.Transaction = tx;
        cmd.CommandText = "SELECT RelativePath, ServerFolderId, ParentRelativePath, LastAppliedSequence, UpdatedAt FROM LocalFolders WHERE ServerFolderId = $id;";
        cmd.Parameters.AddWithValue("$id", serverId.ToString());
        using var reader = cmd.ExecuteReader();
        if (!reader.Read()) return null;
        return Map(reader);
    }

    public List<LocalFolderRow> GetChildren(string parentRelativePath, SqliteTransaction? tx = null)
    {
        using var cmd = connection.CreateCommand();
        if (tx is not null) cmd.Transaction = tx;
        cmd.CommandText = "SELECT RelativePath, ServerFolderId, ParentRelativePath, LastAppliedSequence, UpdatedAt FROM LocalFolders WHERE ParentRelativePath = $parent ORDER BY RelativePath;";
        cmd.Parameters.AddWithValue("$parent", LocalPath.Canonicalize(parentRelativePath));
        using var reader = cmd.ExecuteReader();
        var result = new List<LocalFolderRow>();
        while (reader.Read()) result.Add(Map(reader));
        return result;
    }

    public void Delete(string relativePath, SqliteTransaction? tx = null)
    {
        using var cmd = connection.CreateCommand();
        if (tx is not null) cmd.Transaction = tx;
        cmd.CommandText = "DELETE FROM LocalFolders WHERE RelativePath = $path;";
        cmd.Parameters.AddWithValue("$path", LocalPath.Canonicalize(relativePath));
        cmd.ExecuteNonQuery();
    }

    public void Rename(string oldPath, string newPath, SqliteTransaction? tx = null)
    {
        var from = LocalPath.Canonicalize(oldPath);
        var to = LocalPath.Canonicalize(newPath);
        using var cmd = connection.CreateCommand();
        if (tx is not null) cmd.Transaction = tx;
        cmd.CommandText = "UPDATE LocalFolders SET RelativePath = $to, UpdatedAt = $now WHERE RelativePath = $from;";
        cmd.Parameters.AddWithValue("$to", to);
        cmd.Parameters.AddWithValue("$from", from);
        cmd.Parameters.AddWithValue("$now", DateTime.UtcNow.ToString("O"));
        if (cmd.ExecuteNonQuery() != 1)
            throw new InvalidOperationException($"Local folder '{from}' not found for rename.");
    }

    private static LocalFolderRow Map(SqliteDataReader r) => new(
        r.GetString(0),
        string.IsNullOrEmpty(r.IsDBNull(1) ? null : r.GetString(1)) ? null : Guid.Parse(r.GetString(1)),
        r.IsDBNull(2) ? null : r.GetString(2),
        r.GetInt64(3),
        DateTime.Parse(r.GetString(4), null, System.Globalization.DateTimeStyles.RoundtripKind));
}
