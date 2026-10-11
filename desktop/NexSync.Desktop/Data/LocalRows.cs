using System;
namespace NexSync.Desktop.Data;

public enum PendingOpType
{
    CreateFile,
    ModifyFile,
    DeleteFile,
    MoveFile,
    CreateFolder,
    DeleteFolder,
    MoveFolder,
    RenameFolder
}

public enum PendingOpStatus
{
    Pending,
    InProgress,
    Failed,
    Done
}

public sealed record LocalFolderRow(
    string RelativePath,
    Guid? ServerFolderId,
    string? ParentRelativePath,
    long LastAppliedSequence,
    DateTime UpdatedAt);

public sealed record LocalFileRow(
    string RelativePath,
    Guid? ServerFileId,
    Guid? ServerFolderId,
    string? Hash,
    long? Size,
    long LastAppliedSequence,
    DateTime UpdatedAt);

public sealed record PendingOperationRow(
    Guid OperationId,
    PendingOpType OpType,
    string RelPath,
    string? NewRelPath,
    Guid? ServerId,
    Guid? ParentServerId,
    int RetryCount,
    PendingOpStatus Status,
    DateTime CreatedAt);

public sealed record ProcessedOperationRow(
    Guid OperationId,
    string ResultSummary,
    DateTime CreatedAt);

