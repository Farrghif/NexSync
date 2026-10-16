using System.Collections.ObjectModel;
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
    private string _sortMode = "Name (A-Z)";

    public BrowserPage(FileService files, AuthService auth)
    {
        InitializeComponent();
        _files = files;
        _auth = auth;
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
                    UpdatedAt = DateTime.TryParse(f.UpdatedAt, out var u) ? u : DateTime.UtcNow
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
                    UpdatedAt = DateTime.TryParse(f.UpdatedAt, out var u) ? u : DateTime.UtcNow
                });
            }

            _allItems = list;
            ApplyFiltersAndSort();
        }
        catch (Exception ex)
        {
            await DisplayAlert("Error", ex.Message, "OK");
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

        var result = filtered.ToList();
        ItemsCollection.ItemsSource = new ObservableCollection<UiItem>(result);

        // Search clear affordance
        if (SearchClearBtn != null)
            SearchClearBtn.IsVisible = !string.IsNullOrEmpty(q);

        // Count pill
        if (DeviceFilterCountLabel != null)
            DeviceFilterCountLabel.Text = $"{result.Count}";

        // EmptyView helpers
        if (EmptyTitleLabel != null && EmptySubtitleLabel != null && EmptyActionBtn != null)
        {
            if (result.Count == 0 && !string.IsNullOrEmpty(q))
            {
                EmptyTitleLabel.Text = $"No matching files for '{q}'";
                EmptySubtitleLabel.Text = "Try a different search term or clear the filter.";
                EmptyActionBtn.IsVisible = true;
                EmptyActionBtn.Text = "Clear search";
            }
            else if (result.Count == 0 && _filterThisDeviceOnly)
            {
                EmptyTitleLabel.Text = "No files from this device here";
                EmptySubtitleLabel.Text = "Switch to All Files to see everything in this folder.";
                EmptyActionBtn.IsVisible = true;
                EmptyActionBtn.Text = "Show all files";
            }
            else if (result.Count == 0)
            {
                EmptyTitleLabel.Text = "This folder is empty";
                EmptySubtitleLabel.Text = "Upload files or create a folder to get started.";
                EmptyActionBtn.IsVisible = false;
            }
            else
            {
                // has results — keep defaults invisible
                EmptyActionBtn.IsVisible = false;
            }
        }
    }

    private void UpdateDeviceFilterUi()
    {
        var currentDev = SecureStore.CurrentDeviceId;
        var shortId = currentDev?.Length >= 8 ? currentDev[..8] + "…" : "device";
        if (_filterThisDeviceOnly)
        {
            DeviceFilterTitleLabel.Text = $"Showing: This Device ({shortId})";
            DeviceFilterSubtitleLabel.Text = "Only files originating from this Android device";
            DeviceFilterToggleBtn.Text = "Show All";
            DeviceFilterToggleBtn.BackgroundColor = Color.FromArgb("#C62B3C");
        }
        else
        {
            DeviceFilterTitleLabel.Text = $"Device: {shortId}";
            DeviceFilterSubtitleLabel.Text = "Showing all files across every device";
            DeviceFilterToggleBtn.Text = "This Device";
            DeviceFilterToggleBtn.BackgroundColor = Color.FromArgb("#5B5BD6");
        }

        UpdateSyncStatus();
    }

    private void UpdateSyncStatus()
    {
        if (SyncStatusLabel == null || SyncStatusDot == null || SyncStatusDetailLabel == null)
            return;

        try
        {
            var offline = Connectivity.Current.NetworkAccess != NetworkAccess.Internet;
            if (offline)
            {
                SyncStatusLabel.Text = "Offline";
                SyncStatusDetailLabel.Text = "No internet connection";
                SyncStatusDot.BackgroundColor = Color.FromArgb("#8B90A0");
                return;
            }
        }
        catch
        {
            // Connectivity not available in some test contexts — assume online
        }

        if (Refresher != null && Refresher.IsRefreshing)
        {
            SyncStatusLabel.Text = "Syncing";
            SyncStatusDetailLabel.Text = "Refreshing…";
            if (SyncStatusDot != null) SyncStatusDot.BackgroundColor = Color.FromArgb("#F59E0B");
        }
        else
        {
            SyncStatusLabel.Text = "All synced";
            SyncStatusDetailLabel.Text = "Up to date";
            SyncStatusDot.BackgroundColor = Color.FromArgb("#1DB954");
        }
    }

    private void OnDeviceFilterToggleClicked(object sender, EventArgs e)
    {
        _filterThisDeviceOnly = !_filterThisDeviceOnly;
        UpdateDeviceFilterUi();
        ApplyFiltersAndSort();
    }

    private void OnSearchTextChanged(object sender, TextChangedEventArgs e)
    {
        ApplyFiltersAndSort();
    }

    private void OnSearchClearClicked(object sender, EventArgs e)
    {
        if (_filterThisDeviceOnly && EmptyActionBtn != null && EmptyActionBtn.IsVisible && string.IsNullOrEmpty(SearchBox?.Text))
        {
            // Empty due to device filter — toggle it
            _filterThisDeviceOnly = false;
            UpdateDeviceFilterUi();
        }
        if (SearchBox != null)
            SearchBox.Text = string.Empty;
        ApplyFiltersAndSort();
    }

    private async void OnRefreshing(object sender, EventArgs e)
    {
        await LoadCurrentFolderAsync();
    }

    private async void OnBackClicked(object sender, EventArgs e)
    {
        if (_navStack.Count > 0)
        {
            _navStack.Pop();
            await LoadCurrentFolderAsync();
        }
    }

    private async void OnItemTapped(object sender, EventArgs e)
    {
        if (sender is Element elem && elem.BindingContext is UiItem item)
        {
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

    private async Task ShowFileActionsAsync(UiItem file)
    {
        var choice = await DisplayActionSheet(file.Name, "Cancel", null, "Download & Open", "Rename", "Delete");
        if (choice == "Download & Open")
        {
            try
            {
                var localPath = await _files.GetDownloadPathAsync(file.Id, file.Name);
                await Launcher.OpenAsync(new OpenFileRequest
                {
                    File = new ReadOnlyFile(localPath),
                    Title = file.Name
                });
            }
            catch (Exception ex)
            {
                await DisplayAlert("Error", ex.Message, "OK");
            }
        }
        else if (choice == "Rename")
        {
            await PromptRenameAsync(file);
        }
        else if (choice == "Delete")
        {
            await PromptDeleteAsync(file);
        }
    }

    private async void OnRenameItemInvoked(object sender, EventArgs e)
    {
        if (sender is SwipeItem si && si.BindingContext is UiItem item)
            await PromptRenameAsync(item);
    }

    private async void OnDeleteItemInvoked(object sender, EventArgs e)
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
            await DisplayAlert("Error", ex.Message, "OK");
        }
    }

    private async Task PromptDeleteAsync(UiItem item)
    {
        var type = item.IsFolder ? "folder and all its contents" : "file";
        var ok = await DisplayAlert("Delete", $"Are you sure you want to delete this {type}?", "Delete", "Cancel");
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
            await DisplayAlert("Error", ex.Message, "OK");
        }
    }

    private async void OnNewFolderClicked(object sender, EventArgs e)
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
            await DisplayAlert("Error", ex.Message, "OK");
        }
    }

    private async void OnUploadClicked(object sender, EventArgs e)
    {
        try
        {
            var res = await FilePicker.Default.PickMultipleAsync();
            if (res == null) return;

            foreach (var file in res)
            {
                await using var stream = await file.OpenReadAsync();
                await _files.UploadAsync(stream, file.FileName, CurrentFolderId, file.ContentType ?? "application/octet-stream");
            }

            await LoadCurrentFolderAsync();
        }
        catch (Exception ex)
        {
            await DisplayAlert("Error", ex.Message, "OK");
        }
    }

    private async void OnSortClicked(object sender, EventArgs e)
    {
        var choices = new[]
        {
            "Name (A-Z)",
            "Name (Z-A)",
            "Latest Modified",
            "Oldest Modified",
            "Size (Largest)",
            "Size (Smallest)"
        };
        var pick = await DisplayActionSheet("Sort Items", "Cancel", null, choices);
        if (pick != null && pick != "Cancel")
        {
            _sortMode = pick;
            ApplyFiltersAndSort();
        }
    }

    private async void OnAccountClicked(object sender, EventArgs e)
    {
        var email = SecureStore.CurrentEmail ?? "Unknown";
        var dev = SecureStore.CurrentDeviceId ?? "None";
        var action = await DisplayActionSheet($"Account: {email}", "Cancel", "Sign Out", $"Device ID: {dev}");
        if (action == "Sign Out")
        {
            await _auth.LogoutAsync();
            await Navigation.PushModalAsync(new NavigationPage(new LoginPage(_auth)));
        }
    }
}
