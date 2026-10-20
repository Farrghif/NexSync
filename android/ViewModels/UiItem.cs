using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace NexSync.Android.ViewModels;

public sealed class UiItem : INotifyPropertyChanged
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

    private bool _isFavorite;
    public bool IsFavorite
    {
        get => _isFavorite;
        set
        {
            if (_isFavorite != value)
            {
                _isFavorite = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(DisplayNameWithStar));
                OnPropertyChanged(nameof(IsStarVisible));
            }
        }
    }

    private bool _isSelected;
    public bool IsSelected
    {
        get => _isSelected;
        set
        {
            if (_isSelected != value)
            {
                _isSelected = value;
                OnPropertyChanged();
            }
        }
    }

    private bool _selectionModeActive;
    public bool SelectionModeActive
    {
        get => _selectionModeActive;
        set
        {
            if (_selectionModeActive != value)
            {
                _selectionModeActive = value;
                OnPropertyChanged();
            }
        }
    }

    public string IconText => IsFolder ? "📁" : "📄";
    public bool IsStarVisible => _isFavorite;
    public string DisplayNameWithStar => _isFavorite ? $"{Name} ⭐" : Name;

    public string SubtitleText => IsFolder
        ? "Folder"
        : $"{FormatSize(Size)} · {ContentType ?? "File"}";

    // Green = synced (server confirmed), grey for folders/pending
    public string SyncDotColor => IsFolder ? "#8B90A0" : "#1DB954";

    public static string FormatSize(long bytes)
    {
        if (bytes < 1024) return $"{bytes} B";
        if (bytes < 1024 * 1024) return $"{(bytes / 1024.0):F1} KB";
        if (bytes < 1024 * 1024 * 1024) return $"{(bytes / (1024.0 * 1024)):F1} MB";
        return $"{(bytes / (1024.0 * 1024 * 1024)):F2} GB";
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
