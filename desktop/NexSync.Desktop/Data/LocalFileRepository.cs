using System;
using System.Collections.Generic;
using Microsoft.Data.Sqlite;

namespace NexSync.Desktop.Data;

public sealed class LocalFileRepository(SqliteConnection connection)
{
    public void Upsert(LocalFileRow row, SqliteTransaction? tx = null)
    {
        using var cmd = connection.CreateCommand();
        if (tx is not null) cmd.Transaction = tx;
        cmd.CommandText = """
            INSERT INTO LocalFiles (RelativePath, ServerFileId, ServerFolderId, Hash, Size, LastAppliedSequence, UpdatedAt)
            VALUES ($path, $fileId, $folderId, $hash, $size, $seq, $updated)
            ON CONFLICT(RelativePath) DO UPDATE SET
                ServerFileId = excluded.ServerFileId,
                ServerFolderId = excluded.ServerFolderId,
                Hash = excluded.Hash,
                Size = excluded.Size,
                LastAppliedSequence = excluded.LastAppliedSequence,
                UpdatedAt = excluded.UpdatedAt;
            """;
        cmd.Parameters.AddWithValue("$path", LocalPath.Canonicalize(row.RelativePath));
        cmd.Parameters.AddWithValue("$fileId", (object?)row.ServerFileId?.ToString() ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$folderId", (object?)row.ServerFolderId?.ToString() ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$hash", (object?)row.Hash ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$size", (object?)row.Size ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$seq", row.LastAppliedSequence);
        cmd.Parameters.AddWithValue("$updated", row.UpdatedAt.ToString("O"));
        cmd.ExecuteNonQuery();
    }

    public LocalFileRow? GetByPath(string relativePath, SqliteTransaction? tx = null)
    {
        using var cmd = connection.CreateCommand();
        if (tx is not null) cmd.Transaction = tx;
        cmd.CommandText = "SELECT RelativePath, ServerFileId, ServerFolderId, Hash, Size, LastAppliedSequence, UpdatedAt FROM LocalFiles WHERE RelativePath = $path;";
        cmd.Parameters.AddWithValue("$path", LocalPath.Canonicalize(relativePath));
        using var reader = cmd.ExecuteReader();
        if (!reader.Read()) return null;
        return Map(reader);
    }

    public LocalFileRow? GetByServerId(Guid serverId, SqliteTransaction? tx = null)
    {
        using var cmd = connection.CreateCommand();
        if (tx is not null) cmd.Transaction = tx;
        cmd.CommandText = "SELECT RelativePath, ServerFileId, ServerFolderId, Hash, Size, LastAppliedSequence, UpdatedAt FROM LocalFiles WHERE ServerFileId = $id;";
        cmd.Parameters.AddWithValue("$id", serverId.ToString());
        using var reader = cmd.ExecuteReader();
        if (!reader.Read()) return null;
        return Map(reader);
    }

    public List<LocalFileRow> GetByFolderPath(string folderRelativePath, SqliteTransaction? tx = null)
    {
        var prefix = LocalPath.Canonicalize(folderRelativePath) + "/";
        using var cmd = connection.CreateCommand();
        if (tx is not null) cmd.Transaction = tx;
        cmd.CommandText = "SELECT RelativePath, ServerFileId, ServerFolderId, Hash, Size, LastAppliedSequence, UpdatedAt FROM LocalFiles WHERE RelativePath LIKE $prefix || '%' ESCAPE '\\' ORDER BY RelativePath;";
        cmd.Parameters.AddWithValue("$prefix", prefix.Replace(@"\", @"\\").Replace("%", @"\%").Replace("_", @"\_"));
        using var reader = cmd.ExecuteReader();
        var result = new List<LocalFileRow>();
        while (reader.Read()) result.Add(Map(reader));
        return result;
    }

    public void Delete(string relativePath, SqliteTransaction? tx = null)
    {
        using var cmd = connection.CreateCommand();
        if (tx is not null) cmd.Transaction = tx;
        cmd.CommandText = "DELETE FROM LocalFiles WHERE RelativePath = $path;";
        cmd.Parameters.AddWithValue("$path", LocalPath.Canonicalize(relativePath));
        cmd.ExecuteNonQuery();
    }

    public void Move(string oldPath, string newPath, SqliteTransaction? tx = null)
    {
        var from = LocalPath.Canonicalize(oldPath);
        var to = LocalPath.Canonicalize(newPath);
        using var cmd = connection.CreateCommand();
        if (tx is not null) cmd.Transaction = tx;
        cmd.CommandText = "UPDATE LocalFiles SET RelativePath = $to, UpdatedAt = $now WHERE RelativePath = $from;";
        cmd.Parameters.AddWithValue("$to", to);
        cmd.Parameters.AddWithValue("$from", from);
        cmd.Parameters.AddWithValue("$now", DateTime.UtcNow.ToString("O"));
        if (cmd.ExecuteNonQuery() != 1)
            throw new InvalidOperationException($"Local file '{from}' not found for move.");
    }

    private static LocalFileRow Map(SqliteDataReader r) => new(
        r.GetString(0),
        r.IsDBNull(1) ? null : Guid.Parse(r.GetString(1)),
        r.IsDBNull(2) ? null : Guid.Parse(r.GetString(2)),
        r.IsDBNull(3) ? null : r.GetString(3),
        r.IsDBNull(4) ? null : r.GetInt64(4),
        r.GetInt64(5),
        DateTime.Parse(r.GetString(6), null, System.Globalization.DateTimeStyles.RoundtripKind));
}
