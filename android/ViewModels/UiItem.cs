namespace NexSync.Android.ViewModels;

public sealed class UiItem
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public required bool IsFolder { get; init; }
    public string? ParentOrFolderId { get; init; }
    public long Size { get; init; }
    public string? ContentType { get; init; }
    public string? OriginDeviceId { get; init; }
    public DateTime CreatedAt { get; init; }
    public DateTime UpdatedAt { get; init; }

    public string IconText => IsFolder ? "📁" : "📄";

    public string SubtitleText => IsFolder
        ? "Folder"
        : $"{FormatSize(Size)} · {ContentType}";

    // Green = synced (server confirmed), grey for folders/pending
    public string SyncDotColor => IsFolder ? "#8B90A0" : "#1DB954";

    private static string FormatSize(long bytes)
    {
        if (bytes < 1024) return $"{bytes} B";
        if (bytes < 1024 * 1024) return $"{(bytes / 1024.0):F1} KB";
        if (bytes < 1024 * 1024 * 1024) return $"{(bytes / (1024.0 * 1024)):F1} MB";
        return $"{(bytes / (1024.0 * 1024 * 1024)):F2} GB";
    }
}
