using System.Collections.ObjectModel;
using System.Text.Json;
using NexSync.Android.Models;
using NexSync.Android.Services;
using NexSync.Android.ViewModels;

namespace NexSync.Android.Pages;

public partial class BrowserPage : ContentPage
{
    private readonly FileService _files;
    private readonly AuthService _auth;

    private readonly Stack<(string? Id, string Name)> _navStack = new();
    private List<UiItem> _allItems = new();
    private bool _filterThisDeviceOnly = false;
    private string _filterMode = "All"; // "All", "Favorites", "Recent"
    private string _sortMode = "Name (A-Z)";
    private bool _isSelectMode = false;

    private HashSet<string> _favorites = new();
    private List<string> _recentIds = new();

    private const string PrefFavKey = "nexsync_fav";
    private const string PrefRecentKey = "nexsync_recent";
    private const int MaxRecent = 20;

    public BrowserPage(FileService files, AuthService auth)
    {
        InitializeComponent();
        _files = files;
        _auth = auth;
        LoadStoredPreferences();
    }

    private void LoadStoredPreferences()
    {
        try
        {
            var favRaw = Preferences.Default.Get(PrefFavKey, "");
            if (!string.IsNullOrWhiteSpace(favRaw))
            {
                var arr = JsonSerializer.Deserialize<string[]>(favRaw);
                if (arr is not null) _favorites = new HashSet<string>(arr);
            }
        }
        catch { _favorites = new(); }

        try
        {
            var recRaw = Preferences.Default.Get(PrefRecentKey, "");
            if (!string.IsNullOrWhiteSpace(recRaw))
            {
                var arr = JsonSerializer.Deserialize<string[]>(recRaw);
                if (arr is not null) _recentIds = arr.Take(MaxRecent).ToList();
            }
        }
        catch { _recentIds = new(); }
    }

    private void SaveFavorites()
    {
        try
        {
            Preferences.Default.Set(PrefFavKey, JsonSerializer.Serialize(_favorites.ToArray()));
        }
        catch { }
    }

    private void PushRecent(string id)
    {
        try
        {
            _recentIds.Remove(id);
            _recentIds.Insert(0, id);
            if (_recentIds.Count > MaxRecent)
                _recentIds = _recentIds.Take(MaxRecent).ToList();
            Preferences.Default.Set(PrefRecentKey, JsonSerializer.Serialize(_recentIds.ToArray()));
        }
        catch { }
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        if (!_auth.IsSignedIn)
        {
            await Navigation.PushModalAsync(new NavigationPage(new LoginPage(_auth)));
            return;
        }

        UpdateDeviceFilterUi();
        UpdateSyncStatus();
        await LoadCurrentFolderAsync();
    }

    private string? CurrentFolderId => _navStack.Count > 0 ? _navStack.Peek().Id : null;
    private string CurrentFolderName => _navStack.Count > 0 ? _navStack.Peek().Name : "Root";

    private async Task LoadCurrentFolderAsync()
    {
        Refresher.IsRefreshing = true;
        UpdateSyncStatus();
        try
        {
            var contents = await _files.GetContentsAsync(CurrentFolderId);
            var list = new List<UiItem>();

            foreach (var f in contents.Folders)
            {
                list.Add(new UiItem
                {
                    Id = f.Id,
                    Name = f.Name,
                    IsFolder = true,
                    ParentOrFolderId = f.ParentFolderId,
                    Size = 0,
                    ContentType = "Folder",
                    OriginDeviceId = f.OriginDeviceId,
                    CreatedAt = DateTime.TryParse(f.CreatedAt, out var c) ? c : DateTime.UtcNow,
                    UpdatedAt = DateTime.TryParse(f.UpdatedAt, out var u) ? u : DateTime.UtcNow,
                    IsFavorite = _favorites.Contains(f.Id),
                    SelectionModeActive = _isSelectMode
                });
            }

            foreach (var f in contents.Files)
            {
                list.Add(new UiItem
                {
                    Id = f.Id,
                    Name = f.Name,
                    IsFolder = false,
                    ParentOrFolderId = f.FolderId,
                    Size = f.Size,
                    ContentType = f.ContentType,
                    OriginDeviceId = f.OriginDeviceId,
                    CreatedAt = DateTime.TryParse(f.CreatedAt, out var c) ? c : DateTime.UtcNow,
                    UpdatedAt = DateTime.TryParse(f.UpdatedAt, out var u) ? u : DateTime.UtcNow,
                    IsFavorite = _favorites.Contains(f.Id),
                    SelectionModeActive = _isSelectMode
                });
            }

            _allItems = list;
            ApplyFiltersAndSort();
        }
        catch (Exception ex)
        {
            await DisplayAlertAsync("Error", ex.Message, "OK");
        }
        finally
        {
            Refresher.IsRefreshing = false;
            CurrentPathLabel.Text = CurrentFolderName;
            BackBtn.IsVisible = _navStack.Count > 0;
            UpdateSyncStatus();
        }
    }

    private void ApplyFiltersAndSort()
    {
        var q = SearchBox?.Text?.Trim().ToLowerInvariant() ?? "";
        var currentDev = SecureStore.CurrentDeviceId;

        var filtered = _allItems.AsEnumerable();

        if (!string.IsNullOrEmpty(q))
            filtered = filtered.Where(x => x.Name.ToLowerInvariant().Contains(q));

        if (_filterThisDeviceOnly && !string.IsNullOrEmpty(currentDev))
            filtered = filtered.Where(x => x.OriginDeviceId == currentDev);

        if (_filterMode == "Favorites")
            filtered = filtered.Where(x => x.IsFavorite);
        else if (_filterMode == "Recent")
        {
            var rank = _recentIds.Select((id, index) => (id, index)).ToDictionary(x => x.id, x => x.index);
            filtered = filtered.Where(x => rank.ContainsKey(x.Id)).OrderBy(x => rank[x.Id]);
        }

        if (_filterMode != "Recent")
        {
            filtered = _sortMode switch
            {
                "Name (A-Z)" => filtered.OrderBy(x => !x.IsFolder).ThenBy(x => x.Name),
                "Name (Z-A)" => filtered.OrderBy(x => !x.IsFolder).ThenByDescending(x => x.Name),
                "Latest Modified" => filtered.OrderBy(x => !x.IsFolder).ThenByDescending(x => x.UpdatedAt),
                "Oldest Modified" => filtered.OrderBy(x => !x.IsFolder).ThenBy(x => x.UpdatedAt),
                "Size (Largest)" => filtered.OrderBy(x => !x.IsFolder).ThenByDescending(x => x.Size),
                "Size (Smallest)" => filtered.OrderBy(x => !x.IsFolder).ThenBy(x => x.Size),
                _ => filtered.OrderBy(x => !x.IsFolder).ThenBy(x => x.Name)
            };
        }

        var result = filtered.ToList();
        ItemsCollection.ItemsSource = result;

        UpdateEmptyState(result.Count, q);
    }

    private void UpdateEmptyState(int count, string query)
    {
        if (count > 0)
        {
            EmptyActionBtn.IsVisible = false;
            return;
        }

        if (!string.IsNullOrEmpty(query))
        {
            EmptyTitleLabel.Text = $"No matches for \"{query}\"";
            EmptySubtitleLabel.Text = "Check your spelling or clear the search.";
            EmptyActionBtn.Text = "Clear search";
            EmptyActionBtn.IsVisible = true;
        }
        else if (_filterMode == "Favorites")
        {
            EmptyTitleLabel.Text = "No favorites yet";
            EmptySubtitleLabel.Text = "Swipe or tap an item to star it as a favorite.";
            EmptyActionBtn.IsVisible = false;
        }
        else if (_filterMode == "Recent")
        {
            EmptyTitleLabel.Text = "No recent files";
            EmptySubtitleLabel.Text = "Files you open or download will appear here.";
            EmptyActionBtn.IsVisible = false;
        }
        else if (_filterThisDeviceOnly)
        {
            EmptyTitleLabel.Text = "No files from this device";
            EmptySubtitleLabel.Text = "Upload from this device or toggle 'This Device' off to view all files.";
            EmptyActionBtn.IsVisible = false;
        }
        else
        {
            EmptyTitleLabel.Text = "This folder is empty";
            EmptySubtitleLabel.Text = "Upload files or create a folder to get started.";
            EmptyActionBtn.IsVisible = false;
        }
    }

    private void UpdateDeviceFilterUi()
    {
        var currentDev = SecureStore.CurrentDeviceId;
        var shortDev = currentDev is not null && currentDev.Length >= 8 ? currentDev[..8] : (currentDev ?? "—");

        if (_filterThisDeviceOnly)
        {
            DeviceFilterTitleLabel.Text = $"Filtered: {shortDev}…";
            DeviceFilterSubtitleLabel.Text = "Showing only files created on this device";
            DeviceFilterToggleBtn.Text = "Show All";
            DeviceFilterCountLabel.Text = "1";
        }
        else
        {
            DeviceFilterTitleLabel.Text = "Showing all files";
            DeviceFilterSubtitleLabel.Text = "Across every device";
            DeviceFilterToggleBtn.Text = "This Device";
            DeviceFilterCountLabel.Text = "All";
        }
    }

    private void UpdateSyncStatus()
    {
        if (Connectivity.Current.NetworkAccess != NetworkAccess.Internet)
        {
            SyncStatusDot.BackgroundColor = Color.FromArgb("#8B90A0");
            SyncStatusLabel.Text = "Offline";
            SyncStatusDetailLabel.Text = "No internet connection";
            return;
        }

        if (Refresher.IsRefreshing)
        {
            SyncStatusDot.BackgroundColor = Color.FromArgb("#CA8A04");
            SyncStatusLabel.Text = "Syncing";
            SyncStatusDetailLabel.Text = "Refreshing contents…";
            return;
        }

        SyncStatusDot.BackgroundColor = Color.FromArgb("#1DB954");
        SyncStatusLabel.Text = "All synced";
        SyncStatusDetailLabel.Text = "Up to date";
    }

    private void OnDeviceFilterToggleClicked(object? sender, EventArgs e)
    {
        _filterThisDeviceOnly = !_filterThisDeviceOnly;
        UpdateDeviceFilterUi();
        ApplyFiltersAndSort();
    }

    private void OnFilterAllClicked(object? sender, EventArgs e)
    {
        _filterMode = "All";
        HighlightPill(FilterAllBtn);
        ApplyFiltersAndSort();
    }

    private void OnFilterFavClicked(object? sender, EventArgs e)
    {
        _filterMode = "Favorites";
        HighlightPill(FilterFavBtn);
        ApplyFiltersAndSort();
    }

    private void OnFilterRecentClicked(object? sender, EventArgs e)
    {
        _filterMode = "Recent";
        HighlightPill(FilterRecentBtn);
        ApplyFiltersAndSort();
    }

    private void HighlightPill(Button activeBtn)
    {
        var inactiveBg = Color.FromArgb("#14161F");
        FilterAllBtn.BackgroundColor = FilterAllBtn == activeBtn ? Color.FromArgb("#5B5BD6") : Colors.Transparent;
        FilterAllBtn.TextColor = FilterAllBtn == activeBtn ? Colors.White : Color.FromArgb("#8B90A0");

        FilterFavBtn.BackgroundColor = FilterFavBtn == activeBtn ? Color.FromArgb("#5B5BD6") : Colors.Transparent;
        FilterFavBtn.TextColor = FilterFavBtn == activeBtn ? Colors.White : Color.FromArgb("#8B90A0");

        FilterRecentBtn.BackgroundColor = FilterRecentBtn == activeBtn ? Color.FromArgb("#5B5BD6") : Colors.Transparent;
        FilterRecentBtn.TextColor = FilterRecentBtn == activeBtn ? Colors.White : Color.FromArgb("#8B90A0");
    }

    private void OnSearchTextChanged(object? sender, TextChangedEventArgs e)
    {
        SearchClearBtn.IsVisible = !string.IsNullOrEmpty(e.NewTextValue);
        ApplyFiltersAndSort();
    }

    private void OnSearchClearClicked(object? sender, EventArgs e)
    {
        SearchBox.Text = string.Empty;
        SearchClearBtn.IsVisible = false;
        ApplyFiltersAndSort();
    }

    private async void OnBackClicked(object? sender, EventArgs e)
    {
        if (_navStack.Count > 0)
        {
            _navStack.Pop();
            await LoadCurrentFolderAsync();
        }
    }

    private async void OnRefreshing(object? sender, EventArgs e)
    {
        await LoadCurrentFolderAsync();
    }

    private async void OnItemTapped(object? sender, EventArgs e)
    {
        if (sender is Element elem && elem.BindingContext is UiItem item)
        {
            if (_isSelectMode)
            {
                item.IsSelected = !item.IsSelected;
                UpdateSelectionCount();
                return;
            }

            if (item.IsFolder)
            {
                _navStack.Push((item.Id, item.Name));
                await LoadCurrentFolderAsync();
            }
            else
            {
                await ShowFileActionsAsync(item);
            }
        }
    }

    private async Task ShowFileActionsAsync(UiItem item)
    {
        var favLabel = item.IsFavorite ? "Remove from Favorites" : "⭐ Add to Favorites";
        var choice = await DisplayActionSheetAsync(item.Name, "Cancel", null,
            "Download & Open", favLabel, "Move to…", "Details", "Rename", "Delete");

        if (choice == "Download & Open")
        {
            try
            {
                PushRecent(item.Id);
                var localPath = await _files.GetDownloadPathAsync(item.Id, item.Name);
                await Launcher.OpenAsync(new OpenFileRequest
                {
                    File = new ReadOnlyFile(localPath),
                    Title = item.Name
                });
            }
            catch (Exception ex)
            {
                await DisplayAlertAsync("Error", ex.Message, "OK");
            }
        }
        else if (choice == favLabel)
        {
            ToggleFavorite(item);
        }
        else if (choice == "Move to…")
        {
            await PromptMoveAsync(item);
        }
        else if (choice == "Details")
        {
            await ShowDetailsAsync(item);
        }
        else if (choice == "Rename")
        {
            await PromptRenameAsync(item);
        }
        else if (choice == "Delete")
        {
            await PromptDeleteAsync(item);
        }
    }

    private void ToggleFavorite(UiItem item)
    {
        item.IsFavorite = !item.IsFavorite;
        if (item.IsFavorite) _favorites.Add(item.Id);
        else _favorites.Remove(item.Id);
        SaveFavorites();
    }

    private void OnFavoriteItemInvoked(object? sender, EventArgs e)
    {
        if (sender is SwipeItem si && si.BindingContext is UiItem item)
        {
            ToggleFavorite(item);
        }
    }

    private async void OnMoveItemInvoked(object? sender, EventArgs e)
    {
        if (sender is SwipeItem si && si.BindingContext is UiItem item)
        {
            await PromptMoveAsync(item);
        }
    }

    private async Task PromptMoveAsync(UiItem item)
    {
        try
        {
            var currentContents = await _files.GetContentsAsync(CurrentFolderId);
            var eligibleFolders = currentContents.Folders.Where(f => f.Id != item.Id).ToList();

            var options = new List<string> { "📁 Root (Top-level)" };
            foreach (var f in eligibleFolders)
                options.Add($"📁 {f.Name}");

            var choice = await DisplayActionSheetAsync($"Move \"{item.Name}\" to:", "Cancel", null, options.ToArray());
            if (string.IsNullOrEmpty(choice) || choice == "Cancel") return;

            string? targetFolderId = null;
            if (choice != "📁 Root (Top-level)")
            {
                var folderName = choice.Replace("📁 ", "").Trim();
                var selectedFolder = eligibleFolders.FirstOrDefault(f => f.Name == folderName);
                if (selectedFolder is not null) targetFolderId = selectedFolder.Id;
            }

            if (item.IsFolder)
                await _files.UpdateFolderAsync(item.Id, item.Name, targetFolderId);
            else
                await _files.UpdateFileAsync(item.Id, item.Name, targetFolderId);

            await DisplayAlertAsync("Moved", $"Moved \"{item.Name}\" successfully.", "OK");
            await LoadCurrentFolderAsync();
        }
        catch (Exception ex)
        {
            await DisplayAlertAsync("Error", ex.Message, "OK");
        }
    }

    private async Task ShowDetailsAsync(UiItem item)
    {
        var msg = $"Name: {item.Name}\n" +
                  $"Type: {(item.IsFolder ? "Folder" : item.ContentType)}\n" +
                  $"Size: {(item.IsFolder ? "—" : UiItem.FormatSize(item.Size))}\n" +
                  $"Location: {CurrentFolderName}\n" +
                  $"Modified: {item.UpdatedAt.ToLocalTime():g}\n" +
                  $"Created: {item.CreatedAt.ToLocalTime():g}\n" +
                  $"Device Origin: {item.OriginDeviceId ?? "Unknown"}\n" +
                  $"Favorite: {(item.IsFavorite ? "Yes ⭐" : "No")}";

        await DisplayAlertAsync("Item Details", msg, "Close");
    }

    private async void OnRenameItemInvoked(object? sender, EventArgs e)
    {
        if (sender is SwipeItem si && si.BindingContext is UiItem item)
            await PromptRenameAsync(item);
    }

    private async void OnDeleteItemInvoked(object? sender, EventArgs e)
    {
        if (sender is SwipeItem si && si.BindingContext is UiItem item)
            await PromptDeleteAsync(item);
    }

    private async Task PromptRenameAsync(UiItem item)
    {
        var newName = await DisplayPromptAsync("Rename", $"Enter new name for {item.Name}:", initialValue: item.Name);
        if (string.IsNullOrWhiteSpace(newName) || newName == item.Name) return;

        try
        {
            if (item.IsFolder)
                await _files.UpdateFolderAsync(item.Id, newName.Trim(), item.ParentOrFolderId);
            else
                await _files.UpdateFileAsync(item.Id, newName.Trim(), item.ParentOrFolderId);

            await LoadCurrentFolderAsync();
        }
        catch (Exception ex)
        {
            await DisplayAlertAsync("Error", ex.Message, "OK");
        }
    }

    private async Task PromptDeleteAsync(UiItem item)
    {
        var type = item.IsFolder ? "folder and all its contents" : "file";
        var ok = await DisplayAlertAsync("Delete", $"Are you sure you want to delete this {type}?", "Delete", "Cancel");
        if (!ok) return;

        try
        {
            if (item.IsFolder)
                await _files.DeleteFolderAsync(item.Id);
            else
                await _files.DeleteFileAsync(item.Id);

            await LoadCurrentFolderAsync();
        }
        catch (Exception ex)
        {
            await DisplayAlertAsync("Error", ex.Message, "OK");
        }
    }

    private async void OnNewFolderClicked(object? sender, EventArgs e)
    {
        var name = await DisplayPromptAsync("New Folder", "Enter folder name:");
        if (string.IsNullOrWhiteSpace(name)) return;

        try
        {
            await _files.CreateFolderAsync(name.Trim(), CurrentFolderId);
            await LoadCurrentFolderAsync();
        }
        catch (Exception ex)
        {
            await DisplayAlertAsync("Error", ex.Message, "OK");
        }
    }

    private async void OnUploadClicked(object? sender, EventArgs e)
    {
        try
        {
            var files = await FilePicker.Default.PickMultipleAsync();
            if (files == null) return;

            foreach (var f in files)
            {
                if (f == null) continue;
                using var stream = await f.OpenReadAsync();
                await _files.UploadAsync(stream, f.FileName, CurrentFolderId, f.ContentType ?? "application/octet-stream");
            }

            await LoadCurrentFolderAsync();
        }
        catch (Exception ex)
        {
            await DisplayAlertAsync("Upload Error", ex.Message, "OK");
        }
    }

    // --- Multi-select & Bulk operations ---
    private void OnSelectToggleClicked(object? sender, EventArgs e)
    {
        _isSelectMode = !_isSelectMode;
        SelectToolbarBtn.Text = _isSelectMode ? "Done" : "Select";
        SelectionBar.IsVisible = _isSelectMode;

        foreach (var it in _allItems)
        {
            it.SelectionModeActive = _isSelectMode;
            if (!_isSelectMode) it.IsSelected = false;
        }

        UpdateSelectionCount();
    }

    private void OnExitSelectModeClicked(object? sender, EventArgs e)
    {
        _isSelectMode = false;
        SelectToolbarBtn.Text = "Select";
        SelectionBar.IsVisible = false;

        foreach (var it in _allItems)
        {
            it.SelectionModeActive = false;
            it.IsSelected = false;
        }
    }

    private void OnSelectAllClicked(object? sender, EventArgs e)
    {
        var anyUnselected = _allItems.Any(x => !x.IsSelected);
        foreach (var it in _allItems)
            it.IsSelected = anyUnselected;
        UpdateSelectionCount();
    }

    private void UpdateSelectionCount()
    {
        var count = _allItems.Count(x => x.IsSelected);
        SelectionCountLabel.Text = $"{count} selected";
    }

    private async void OnBulkDownloadClicked(object? sender, EventArgs e)
    {
        var selectedFiles = _allItems.Where(x => x.IsSelected && !x.IsFolder).ToList();
        if (selectedFiles.Count == 0)
        {
            await DisplayAlertAsync("Download", "No files selected (folders cannot be downloaded directly).", "OK");
            return;
        }

        int ok = 0;
        foreach (var f in selectedFiles)
        {
            try
            {
                await _files.GetDownloadPathAsync(f.Id, f.Name);
                PushRecent(f.Id);
                ok++;
            }
            catch { }
        }

        await DisplayAlertAsync("Download Completed", $"Downloaded {ok} files to app cache.", "OK");
    }

    private async void OnBulkDeleteClicked(object? sender, EventArgs e)
    {
        var selected = _allItems.Where(x => x.IsSelected).ToList();
        if (selected.Count == 0) return;

        var ok = await DisplayAlertAsync("Delete", $"Delete {selected.Count} selected items? This cannot be undone.", "Delete", "Cancel");
        if (!ok) return;

        int deleted = 0;
        foreach (var item in selected)
        {
            try
            {
                if (item.IsFolder) await _files.DeleteFolderAsync(item.Id);
                else await _files.DeleteFileAsync(item.Id);
                deleted++;
            }
            catch { }
        }

        OnExitSelectModeClicked(sender, e);
        await LoadCurrentFolderAsync();
    }

    private async void OnSortClicked(object? sender, EventArgs e)
    {
        var choice = await DisplayActionSheetAsync("Sort by", "Cancel", null,
            "Name (A-Z)", "Name (Z-A)",
            "Latest Modified", "Oldest Modified",
            "Size (Largest)", "Size (Smallest)");

        if (string.IsNullOrEmpty(choice) || choice == "Cancel") return;
        _sortMode = choice;
        ApplyFiltersAndSort();
    }

    private async void OnAccountClicked(object? sender, EventArgs e)
    {
        var email = _auth.CurrentEmail ?? "Unknown";
        var dev = SecureStore.CurrentDeviceId ?? "—";
        var choice = await DisplayActionSheetAsync(
            $"Account: {email}", "Cancel", null,
            $"Device ID: {dev}", "Sign Out");

        if (choice == "Sign Out")
        {
            await _auth.LogoutAsync();
            await Navigation.PushModalAsync(new NavigationPage(new LoginPage(_auth)));
        }
    }
}
