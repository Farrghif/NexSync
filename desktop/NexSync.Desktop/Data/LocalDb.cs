using System;
using System.Collections.Generic;
using System.Threading;
using Microsoft.Data.Sqlite;

namespace NexSync.Desktop.Data;

public sealed class LocalDb : IDisposable
{
    private readonly SqliteConnection _connection;
    private bool _disposed;

    public SqliteConnection Connection => _connection;

    public LocalDb(string databasePath)
    {
        var dir = System.IO.Path.GetDirectoryName(databasePath);
        if (!string.IsNullOrEmpty(dir))
            System.IO.Directory.CreateDirectory(dir);
        _connection = new SqliteConnection($"Data Source={databasePath}");
        _connection.Open();
        using var pragma = _connection.CreateCommand();
        pragma.CommandText = "PRAGMA journal_mode=WAL; PRAGMA foreign_keys=ON;";
        pragma.ExecuteNonQuery();
        InitializeSchema();
    }

    private void InitializeSchema()
    {
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = """
            CREATE TABLE IF NOT EXISTS LocalFolders (
                RelativePath TEXT PRIMARY KEY,
                ServerFolderId TEXT NULL,
                ParentRelativePath TEXT NULL,
                LastAppliedSequence INTEGER NOT NULL DEFAULT 0,
                UpdatedAt TEXT NOT NULL
            );
            CREATE INDEX IF NOT EXISTS IX_LocalFolders_ServerFolderId ON LocalFolders(ServerFolderId);
            CREATE TABLE IF NOT EXISTS LocalFiles (
                RelativePath TEXT PRIMARY KEY,
                ServerFileId TEXT NULL,
                ServerFolderId TEXT NULL,
                Hash TEXT NULL,
                Size INTEGER NULL,
                LastAppliedSequence INTEGER NOT NULL DEFAULT 0,
                UpdatedAt TEXT NOT NULL
            );
            CREATE INDEX IF NOT EXISTS IX_LocalFiles_ServerFileId ON LocalFiles(ServerFileId);
            CREATE TABLE IF NOT EXISTS PendingOperations (
                OperationId TEXT PRIMARY KEY,
                OpType TEXT NOT NULL,
                RelPath TEXT NOT NULL,
                NewRelPath TEXT NULL,
                ServerId TEXT NULL,
                ParentServerId TEXT NULL,
                RetryCount INTEGER NOT NULL DEFAULT 0,
                Status TEXT NOT NULL DEFAULT 'Pending',
                CreatedAt TEXT NOT NULL
            );
            CREATE INDEX IF NOT EXISTS IX_PendingOperations_Status_Created ON PendingOperations(Status, CreatedAt, OperationId);
            CREATE TABLE IF NOT EXISTS ProcessedOperations (
                OperationId TEXT PRIMARY KEY,
                ResultSummary TEXT NOT NULL,
                CreatedAt TEXT NOT NULL
            );
            CREATE TABLE IF NOT EXISTS SyncState (
                Key TEXT PRIMARY KEY,
                Value TEXT NOT NULL
            );
            """;
        cmd.ExecuteNonQuery();
    }

    public SqliteTransaction BeginTransaction() => _connection.BeginTransaction();

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _connection.Dispose();
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
    }
}
