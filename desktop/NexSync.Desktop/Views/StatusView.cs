using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Microsoft.Win32;
using NexSync.Desktop.Auth;

namespace NexSync.Desktop.Views;

public sealed class StatusView : UserControl
{
    private readonly TextBox _log;
    private readonly TextBlock _statusLabel;
    private readonly TextBlock _statusPath;
    private readonly Border _dot;
    private readonly TextBlock _lastSync;
    private readonly AppState _state;
    private readonly DesktopAuthService _auth;

    // Tabs
    private readonly Border _tabFilesBtn;
    private readonly Border _tabSyncBtn;
    private readonly TextBlock _tabFilesTxt;
    private readonly TextBlock _tabSyncTxt;
    private readonly Grid _filesPanel;
    private readonly ScrollViewer _syncScroll;

    // Files Explorer state
    private string _currentDirectory;
    private string _filterMode = "All"; // "All", "Favorites", "Recent"
    private bool _isSelectMode = false;
    private readonly TextBox _searchBox;
    private readonly Button _searchClearBtn;
    private readonly TextBlock _pathBadge;
    private readonly Button _backBtn;
    private readonly StackPanel _itemsStack;
    private readonly Border _selectionBar;
    private readonly TextBlock _selectionCountLabel;
    private readonly Button _selectModeToggleBtn;
    private readonly Border _pillAll;
    private readonly Border _pillFav;
    private readonly Border _pillRecent;
    private readonly TextBlock _emptyTitle;
    private readonly TextBlock _emptySubtitle;
    private readonly Border _emptyPanel;

    private readonly HashSet<string> _favorites = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<string> _recentPaths = new();
    private readonly List<DesktopFileItem> _currentItems = new();

    private readonly string _favFile;
    private readonly string _recentFile;

    private static SolidColorBrush Br(string hex) => new((Color)ColorConverter.ConvertFromString(hex));
    private static readonly SolidColorBrush BgLight = Br("#F6F7FB");
    private static readonly SolidColorBrush BgWhite = Br("#FFFFFF");
    private static readonly SolidColorBrush BorderLight = Br("#E6E8EF");
    private static readonly SolidColorBrush Brand = Br("#5B5BD6");
    private static readonly SolidColorBrush BrandLight = Br("#EEF0FF");
    private static readonly SolidColorBrush TextDark = Br("#14161D");
    private static readonly SolidColorBrush TextMuted = Br("#8B90A0");
    private static readonly SolidColorBrush Danger = Br("#C62B3C");
    private static readonly SolidColorBrush Success = Br("#15803D");
    private static readonly SolidColorBrush Amber = Br("#CA8A04");
    private static readonly SolidColorBrush SuccessBg = Br("#DCFCE7");
    private static readonly SolidColorBrush AmberBg = Br("#FEF9C3");

    public sealed class DesktopFileItem
    {
        public required string FullPath { get; init; }
        public required string Name { get; init; }
        public required bool IsDirectory { get; init; }
        public long Size { get; init; }
        public DateTime LastWriteTime { get; init; }
        public DateTime CreationTime { get; init; }
        public bool IsFavorite { get; set; }
        public bool IsSelected { get; set; }
    }

    public StatusView(AppState state, DesktopAuthService auth)
    {
        _state = state;
        _auth = auth;
        _currentDirectory = !string.IsNullOrWhiteSpace(state.LocalRoot) && Directory.Exists(state.LocalRoot)
            ? state.LocalRoot!
            : Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

        var dataDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "NexSync");
        Directory.CreateDirectory(dataDir);
        _favFile = Path.Combine(dataDir, "desktop_favorites.json");
        _recentFile = Path.Combine(dataDir, "desktop_recent.json");

        LoadLocalPreferences();

        Background = BgLight;
        FontFamily = new FontFamily("Segoe UI");

        var outer = new Grid { Background = BgLight };
        outer.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // Header
        outer.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // Tab switcher
        outer.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) }); // Tab content

        // --- 1. Header ---
        var header = new Border
        {
            Background = BgWhite,
            BorderBrush = BorderLight,
            BorderThickness = new Thickness(0, 0, 0, 1),
            Padding = new Thickness(20, 14, 20, 14)
        };
        var headerGrid = new Grid();
        headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var email = auth.CurrentUser?.Email ?? "Not signed in";
        var rawDid = auth.DeviceId;
        var deviceShort = rawDid is not null && rawDid.Length >= 8 ? rawDid[..8] : (rawDid ?? "—");
        var initial = string.IsNullOrWhiteSpace(email) ? "?" : email.Trim()[0].ToString().ToUpperInvariant();

        var avatar = new Border
        {
            Width = 36, Height = 36,
            CornerRadius = new CornerRadius(18),
            Background = Brand,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 12, 0)
        };
        avatar.Child = new TextBlock
        {
            Text = initial,
            Foreground = Brushes.White,
            FontWeight = FontWeights.Bold,
            FontSize = 14,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };
        Grid.SetColumn(avatar, 0);
        headerGrid.Children.Add(avatar);

        var whoStack = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        whoStack.Children.Add(new TextBlock { Text = email, FontSize = 13, FontWeight = FontWeights.SemiBold, Foreground = TextDark, TextTrimming = TextTrimming.CharacterEllipsis });
        whoStack.Children.Add(new TextBlock { Text = $"Windows · {deviceShort}…", FontSize = 11, Foreground = TextMuted });
        Grid.SetColumn(whoStack, 1);
        headerGrid.Children.Add(whoStack);

        var topSyncNow = new Button
        {
            Content = "Sync now",
            Background = BrandLight,
            Foreground = Brand,
            FontWeight = FontWeights.SemiBold,
            FontSize = 11,
            Padding = new Thickness(12, 6, 12, 6),
            Cursor = Cursors.Hand,
            VerticalAlignment = VerticalAlignment.Center
        };
        topSyncNow.Click += (_, _) => state.RequestSync();
        Grid.SetColumn(topSyncNow, 2);
        headerGrid.Children.Add(topSyncNow);

        header.Child = headerGrid;
        Grid.SetRow(header, 0);
        outer.Children.Add(header);

        // --- 2. Tab Bar ---
        var tabBar = new Border
        {
            Background = BgWhite,
            BorderBrush = BorderLight,
            BorderThickness = new Thickness(0, 0, 0, 1),
            Padding = new Thickness(20, 8, 20, 8)
        };
        var tabStack = new StackPanel { Orientation = Orientation.Horizontal };

        _tabFilesBtn = CreateTabButton("📁 Files Explorer", true, out _tabFilesTxt);
        _tabSyncBtn = CreateTabButton("⚡ Sync & Activity", false, out _tabSyncTxt);

        _tabFilesBtn.MouseDown += (_, _) => SwitchTab(true);
        _tabSyncBtn.MouseDown += (_, _) => SwitchTab(false);

        tabStack.Children.Add(_tabFilesBtn);
        tabStack.Children.Add(_tabSyncBtn);
        tabBar.Child = tabStack;
        Grid.SetRow(tabBar, 1);
        outer.Children.Add(tabBar);

        // --- 3. Content Panel: Tab 1 (Files Explorer) ---
        _filesPanel = new Grid { Margin = new Thickness(20, 14, 20, 14) };
        _filesPanel.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // Path bar & action row
        _filesPanel.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // Filter pills & Search
        _filesPanel.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // Selection action bar
        _filesPanel.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) }); // File items list

        // Row 0: Breadcrumb & Actions
        var pathBar = new Grid { Margin = new Thickness(0, 0, 0, 10) };
        pathBar.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        pathBar.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        pathBar.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var pathLeft = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        _backBtn = new Button
        {
            Content = "‹ Back",
            Background = BgWhite,
            Foreground = Brand,
            BorderBrush = BorderLight,
            BorderThickness = new Thickness(1),
            FontWeight = FontWeights.Bold,
            FontSize = 11,
            Padding = new Thickness(10, 4, 10, 4),
            Cursor = Cursors.Hand,
            Margin = new Thickness(0, 0, 8, 0),
            Visibility = Visibility.Collapsed
        };
        _backBtn.Click += (_, _) => NavigateBack();
        pathLeft.Children.Add(_backBtn);

        _pathBadge = new TextBlock
        {
            Text = "Root",
            FontSize = 13,
            FontWeight = FontWeights.Bold,
            Foreground = TextDark,
            VerticalAlignment = VerticalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis
        };
        pathLeft.Children.Add(_pathBadge);
        Grid.SetColumn(pathLeft, 0);
        pathBar.Children.Add(pathLeft);

        var pathRight = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        var newFolderBtn = CreateSmallBtn("+ Folder", () => PromptNewFolder());
        var addFilesBtn = CreateSmallBtn("⬆ Add Files", () => PromptAddFiles());
        var openExpBtn = CreateSmallBtn("📂 Explorer", () => OpenCurrentInExplorer());
        var refreshBtn = CreateSmallBtn("🔄", () => RefreshFileList());

        pathRight.Children.Add(newFolderBtn);
        pathRight.Children.Add(addFilesBtn);
        pathRight.Children.Add(openExpBtn);
        pathRight.Children.Add(refreshBtn);
        Grid.SetColumn(pathRight, 2);
        pathBar.Children.Add(pathRight);

        Grid.SetRow(pathBar, 0);
        _filesPanel.Children.Add(pathBar);

        // Row 1: Filter pills & Search input
        var filterSearchRow = new Grid { Margin = new Thickness(0, 0, 0, 10) };
        filterSearchRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        filterSearchRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        filterSearchRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var pillsStack = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        _pillAll = CreateFilterPill("All", true);
        _pillFav = CreateFilterPill("⭐ Favorites", false);
        _pillRecent = CreateFilterPill("🕒 Recent", false);

        _pillAll.MouseDown += (_, _) => SetFilterMode("All");
        _pillFav.MouseDown += (_, _) => SetFilterMode("Favorites");
        _pillRecent.MouseDown += (_, _) => SetFilterMode("Recent");

        pillsStack.Children.Add(_pillAll);
        pillsStack.Children.Add(_pillFav);
        pillsStack.Children.Add(_pillRecent);
        Grid.SetColumn(pillsStack, 0);
        filterSearchRow.Children.Add(pillsStack);

        var searchBorder = new Border
        {
            Background = BgWhite,
            BorderBrush = BorderLight,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(8, 4, 8, 4),
            Margin = new Thickness(10, 0, 10, 0),
            VerticalAlignment = VerticalAlignment.Center
        };
        var searchGrid = new Grid();
        searchGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        searchGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        _searchBox = new TextBox
        {
            BorderThickness = new Thickness(0),
            Background = Brushes.Transparent,
            FontSize = 11,
            Foreground = TextDark,
            VerticalAlignment = VerticalAlignment.Center
        };
        _searchBox.TextChanged += (_, _) =>
        {
            _searchClearBtn.Visibility = string.IsNullOrEmpty(_searchBox.Text) ? Visibility.Collapsed : Visibility.Visible;
            ApplyFilters();
        };
        Grid.SetColumn(_searchBox, 0);
        searchGrid.Children.Add(_searchBox);

        _searchClearBtn = new Button
        {
            Content = "✕",
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            Foreground = TextMuted,
            FontSize = 11,
            Cursor = Cursors.Hand,
            Visibility = Visibility.Collapsed,
            Padding = new Thickness(4, 0, 4, 0)
        };
        _searchClearBtn.Click += (_, _) => { _searchBox.Text = ""; };
        Grid.SetColumn(_searchClearBtn, 1);
        searchGrid.Children.Add(_searchClearBtn);

        searchBorder.Child = searchGrid;
        Grid.SetColumn(searchBorder, 1);
        filterSearchRow.Children.Add(searchBorder);

        _selectModeToggleBtn = new Button
        {
            Content = "Select",
            Background = BgWhite,
            Foreground = Brand,
            BorderBrush = BorderLight,
            BorderThickness = new Thickness(1),
            FontWeight = FontWeights.SemiBold,
            FontSize = 11,
            Padding = new Thickness(10, 4, 10, 4),
            Cursor = Cursors.Hand,
            VerticalAlignment = VerticalAlignment.Center
        };
        _selectModeToggleBtn.Click += (_, _) => ToggleSelectMode();
        Grid.SetColumn(_selectModeToggleBtn, 2);
        filterSearchRow.Children.Add(_selectModeToggleBtn);

        Grid.SetRow(filterSearchRow, 1);
        _filesPanel.Children.Add(filterSearchRow);

        // Row 2: Selection bar
        _selectionBar = new Border
        {
            Background = BrandLight,
            BorderBrush = Brand,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(12, 6, 12, 6),
            Margin = new Thickness(0, 0, 0, 10),
            Visibility = Visibility.Collapsed
        };
        var selGrid = new Grid();
        selGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        selGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        selGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        _selectionCountLabel = new TextBlock
        {
            Text = "0 selected",
            FontWeight = FontWeights.Bold,
            FontSize = 12,
            Foreground = Brand,
            VerticalAlignment = VerticalAlignment.Center
        };
        Grid.SetColumn(_selectionCountLabel, 0);
        selGrid.Children.Add(_selectionCountLabel);

        var selActions = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        var selectAllBtn = CreateSmallBtn("Select All", () =>
        {
            var anyUnsel = _currentItems.Any(x => !x.IsSelected);
            foreach (var item in _currentItems) item.IsSelected = anyUnsel;
            RefreshFileList(keepSelection: true);
        });
        var deleteSelBtn = CreateSmallBtn("Delete Selected", () => BulkDeleteSelected(), isDanger: true);
        var exitSelBtn = CreateSmallBtn("Exit", () => ToggleSelectMode(forceOff: true));

        selActions.Children.Add(selectAllBtn);
        selActions.Children.Add(deleteSelBtn);
        selActions.Children.Add(exitSelBtn);
        Grid.SetColumn(selActions, 2);
        selGrid.Children.Add(selActions);

        _selectionBar.Child = selGrid;
        Grid.SetRow(_selectionBar, 2);
        _filesPanel.Children.Add(_selectionBar);

        // Row 3: Items scroll & list (Drag & drop enabled)
        var itemsScroll = new ScrollViewer
        {
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            AllowDrop = true
        };
        itemsScroll.DragEnter += OnDragOver;
        itemsScroll.DragOver += OnDragOver;
        itemsScroll.Drop += OnFilesDropped;

        var itemsContainer = new Grid();
        _itemsStack = new StackPanel();
        itemsContainer.Children.Add(_itemsStack);

        _emptyPanel = new Border
        {
            Background = Brushes.Transparent,
            Padding = new Thickness(32),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Visibility = Visibility.Collapsed
        };
        var emptyStack = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center };
        emptyStack.Children.Add(new TextBlock { Text = "📂", FontSize = 36, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 0, 0, 8) });
        _emptyTitle = new TextBlock { Text = "Folder is empty", FontSize = 14, FontWeight = FontWeights.Bold, Foreground = TextDark, HorizontalAlignment = HorizontalAlignment.Center };
        _emptySubtitle = new TextBlock { Text = "Drag and drop files here, or click '+ Folder' / 'Add Files'", FontSize = 12, Foreground = TextMuted, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 4, 0, 0) };
        emptyStack.Children.Add(_emptyTitle);
        emptyStack.Children.Add(_emptySubtitle);
        _emptyPanel.Child = emptyStack;
        itemsContainer.Children.Add(_emptyPanel);

        itemsScroll.Content = itemsContainer;
        Grid.SetRow(itemsScroll, 3);
        _filesPanel.Children.Add(itemsScroll);

        Grid.SetRow(_filesPanel, 2);
        outer.Children.Add(_filesPanel);

        // --- 4. Content Panel: Tab 2 (Sync & Activity) ---
        _syncScroll = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, Padding = new Thickness(20), Visibility = Visibility.Collapsed };
        var stack = new StackPanel { MaxWidth = 540, HorizontalAlignment = HorizontalAlignment.Center };

        // Sync status card
        var syncCard = new Border
        {
            Background = BgWhite,
            BorderBrush = BorderLight,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(12),
            Padding = new Thickness(16),
            Margin = new Thickness(0, 0, 0, 16)
        };
        var syncGrid = new Grid();
        syncGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        syncGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        syncGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var syncTop = new Grid();
        syncTop.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        syncTop.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        _dot = new Border
        {
            Width = 10, Height = 10,
            CornerRadius = new CornerRadius(5),
            Background = Success,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 10, 0)
        };
        Grid.SetColumn(_dot, 0);
        syncTop.Children.Add(_dot);

        var labelStack = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        _statusLabel = new TextBlock { FontSize = 13, FontWeight = FontWeights.SemiBold, Foreground = TextDark };
        _statusPath = new TextBlock { FontSize = 11, Foreground = TextMuted, TextTrimming = TextTrimming.CharacterEllipsis, Margin = new Thickness(0, 2, 0, 0) };
        labelStack.Children.Add(_statusLabel);
        labelStack.Children.Add(_statusPath);
        Grid.SetColumn(labelStack, 1);
        syncTop.Children.Add(labelStack);
        Grid.SetRow(syncTop, 0);
        syncGrid.Children.Add(syncTop);

        _lastSync = new TextBlock { FontSize = 11, Foreground = TextMuted, Margin = new Thickness(0, 8, 0, 0) };
        Grid.SetRow(_lastSync, 1);
        syncGrid.Children.Add(_lastSync);

        var actions = new Grid { Margin = new Thickness(0, 12, 0, 0) };
        actions.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        actions.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        var syncNowBorder = new Border { Background = Brand, CornerRadius = new CornerRadius(8), Margin = new Thickness(0, 0, 6, 0) };
        var syncNow = new Button
        {
            Content = "Sync now",
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            Foreground = Brushes.White,
            FontWeight = FontWeights.SemiBold,
            FontSize = 12,
            Padding = new Thickness(12, 8, 12, 8),
            Cursor = Cursors.Hand
        };
        syncNow.Click += (_, _) =>
        {
            AppendLog("Manual sync triggered.");
            state.RequestSync();
        };
        syncNowBorder.Child = syncNow;
        Grid.SetColumn(syncNowBorder, 0);
        actions.Children.Add(syncNowBorder);

        var openFolderBorder = new Border { Background = BgWhite, BorderBrush = BorderLight, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(8), Margin = new Thickness(6, 0, 0, 0) };
        var openFolder = new Button
        {
            Content = "Open folder",
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            Foreground = TextDark,
            FontWeight = FontWeights.SemiBold,
            FontSize = 12,
            Padding = new Thickness(12, 8, 12, 8),
            Cursor = Cursors.Hand
        };
        openFolder.Click += (_, _) => OpenCurrentInExplorer();
        openFolderBorder.Child = openFolder;
        Grid.SetColumn(openFolderBorder, 1);
        actions.Children.Add(openFolderBorder);

        Grid.SetRow(actions, 2);
        syncGrid.Children.Add(actions);

        syncCard.Child = syncGrid;
        stack.Children.Add(syncCard);

        // Activity log card
        stack.Children.Add(new TextBlock
        {
            Text = "ACTIVITY LOG",
            FontSize = 11,
            FontWeight = FontWeights.SemiBold,
            Foreground = TextMuted,
            Margin = new Thickness(4, 0, 0, 6)
        });

        var logBorder = new Border
        {
            Background = BgWhite,
            BorderBrush = BorderLight,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(12),
            Padding = new Thickness(10),
            Margin = new Thickness(0, 0, 0, 16)
        };
        _log = new TextBox
        {
            IsReadOnly = true,
            TextWrapping = TextWrapping.Wrap,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            Height = 160,
            BorderThickness = new Thickness(0),
            Background = Brushes.Transparent,
            Foreground = TextDark,
            FontFamily = new FontFamily("Consolas"),
            FontSize = 11,
            Padding = new Thickness(4)
        };
        logBorder.Child = _log;
        stack.Children.Add(logBorder);

        // Footer actions (Change folder, Sign out)
        var footer = new Grid();
        footer.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        footer.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        var backBorder = new Border { Background = BgWhite, BorderBrush = BorderLight, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(8), Margin = new Thickness(0, 0, 6, 0) };
        var back = new Button
        {
            Content = "Change folder",
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            Foreground = TextDark,
            FontWeight = FontWeights.SemiBold,
            FontSize = 12,
            Padding = new Thickness(12, 10, 12, 10),
            Cursor = Cursors.Hand
        };
        back.Click += (_, _) => state.GoTo(AppScreen.Setup);
        backBorder.Child = back;
        Grid.SetColumn(backBorder, 0);
        footer.Children.Add(backBorder);

        var signOutBorder = new Border { Background = BgWhite, BorderBrush = Br("#FECACA"), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(8), Margin = new Thickness(6, 0, 0, 0) };
        var signOut = new Button
        {
            Content = "Sign out",
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            Foreground = Danger,
            FontWeight = FontWeights.SemiBold,
            FontSize = 12,
            Padding = new Thickness(12, 10, 12, 10),
            Cursor = Cursors.Hand
        };
        signOut.Click += async (_, _) =>
        {
            signOut.IsEnabled = false;
            signOutBorder.Opacity = 0.6;
            try
            {
                await auth.LogoutAsync();
            }
            catch (Exception ex)
            {
                AppendLog("Server sign-out failed (local session cleared anyway): " + ex.Message);
            }
            finally
            {
                state.GoTo(AppScreen.Login);
                signOut.IsEnabled = true;
                signOutBorder.Opacity = 1;
            }
        };
        signOutBorder.Child = signOut;
        Grid.SetColumn(signOutBorder, 1);
        footer.Children.Add(signOutBorder);
        stack.Children.Add(footer);

        _syncScroll.Content = stack;
        Grid.SetRow(_syncScroll, 2);
        outer.Children.Add(_syncScroll);

        Content = outer;

        // State handlers
        state.Changed += () =>
        {
            if (state.SyncRequested)
            {
                AppendLog("Sync signal received.");
                state.ClearSyncRequest();
            }
            RefreshSyncState();
            RefreshFileList();
        };

        RefreshSyncState();
        AppendLog("Ready.");
        if (!string.IsNullOrWhiteSpace(state.LocalRoot))
            AppendLog($"Local folder: {state.LocalRoot}");

        RefreshFileList();
    }

    // --- Tab Navigation ---
    private void SwitchTab(bool isFilesTab)
    {
        _filesPanel.Visibility = isFilesTab ? Visibility.Visible : Visibility.Collapsed;
        _syncScroll.Visibility = isFilesTab ? Visibility.Collapsed : Visibility.Visible;

        _tabFilesBtn.Background = isFilesTab ? BrandLight : Brushes.Transparent;
        _tabFilesTxt.Foreground = isFilesTab ? Brand : TextMuted;
        _tabFilesTxt.FontWeight = isFilesTab ? FontWeights.Bold : FontWeights.SemiBold;

        _tabSyncBtn.Background = !isFilesTab ? BrandLight : Brushes.Transparent;
        _tabSyncTxt.Foreground = !isFilesTab ? Brand : TextMuted;
        _tabSyncTxt.FontWeight = !isFilesTab ? FontWeights.Bold : FontWeights.SemiBold;
    }

    private Border CreateTabButton(string text, bool isActive, out TextBlock labelBlock)
    {
        var border = new Border
        {
            Background = isActive ? BrandLight : Brushes.Transparent,
            CornerRadius = new CornerRadius(16),
            Padding = new Thickness(14, 6, 14, 6),
            Margin = new Thickness(0, 0, 8, 0),
            Cursor = Cursors.Hand
        };
        labelBlock = new TextBlock
        {
            Text = text,
            Foreground = isActive ? Brand : TextMuted,
            FontWeight = isActive ? FontWeights.Bold : FontWeights.SemiBold,
            FontSize = 12
        };
        border.Child = labelBlock;
        return border;
    }

    // --- Filter Pills ---
    private Border CreateFilterPill(string text, bool isActive)
    {
        var border = new Border
        {
            Background = isActive ? Brand : BgWhite,
            BorderBrush = isActive ? Brand : BorderLight,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(14),
            Padding = new Thickness(12, 4, 12, 4),
            Margin = new Thickness(0, 0, 6, 0),
            Cursor = Cursors.Hand
        };
        border.Child = new TextBlock
        {
            Text = text,
            Foreground = isActive ? Brushes.White : TextMuted,
            FontWeight = FontWeights.SemiBold,
            FontSize = 11
        };
        return border;
    }

    private void SetFilterMode(string mode)
    {
        _filterMode = mode;
        UpdatePillState(_pillAll, mode == "All");
        UpdatePillState(_pillFav, mode == "Favorites");
        UpdatePillState(_pillRecent, mode == "Recent");
        ApplyFilters();
    }

    private static void UpdatePillState(Border pill, bool active)
    {
        pill.Background = active ? Brand : BgWhite;
        pill.BorderBrush = active ? Brand : BorderLight;
        if (pill.Child is TextBlock tb)
        {
            tb.Foreground = active ? Brushes.White : TextMuted;
        }
    }

    // --- Files Explorer Core ---
    private void RefreshFileList(bool keepSelection = false)
    {
        var root = _state.LocalRoot;
        if (string.IsNullOrWhiteSpace(root) || !Directory.Exists(root))
        {
            _currentItems.Clear();
            _itemsStack.Children.Clear();
            _emptyPanel.Visibility = Visibility.Visible;
            _emptyTitle.Text = "No local sync folder selected";
            _emptySubtitle.Text = "Click 'Change folder' to set your sync directory.";
            _pathBadge.Text = "No Folder";
            _backBtn.Visibility = Visibility.Collapsed;
            return;
        }

        if (string.IsNullOrWhiteSpace(_currentDirectory) || !Directory.Exists(_currentDirectory))
            _currentDirectory = root;

        // Update back button and path badge
        var isSub = !_currentDirectory.Equals(root, StringComparison.OrdinalIgnoreCase) &&
                    _currentDirectory.StartsWith(root, StringComparison.OrdinalIgnoreCase);
        _backBtn.Visibility = isSub ? Visibility.Visible : Visibility.Collapsed;
        var rel = isSub ? Path.GetRelativePath(root, _currentDirectory) : "Root";
        _pathBadge.Text = rel.StartsWith(".") ? "Root" : rel;

        var prevSelected = keepSelection ? _currentItems.Where(x => x.IsSelected).Select(x => x.FullPath).ToHashSet(StringComparer.OrdinalIgnoreCase) : new HashSet<string>();

        var items = new List<DesktopFileItem>();
        try
        {
            var dirInfo = new DirectoryInfo(_currentDirectory);
            foreach (var d in dirInfo.GetDirectories().OrderBy(x => x.Name))
            {
                if ((d.Attributes & FileAttributes.Hidden) != 0) continue;
                items.Add(new DesktopFileItem
                {
                    FullPath = d.FullName,
                    Name = d.Name,
                    IsDirectory = true,
                    Size = 0,
                    CreationTime = d.CreationTime,
                    LastWriteTime = d.LastWriteTime,
                    IsFavorite = _favorites.Contains(d.FullName),
                    IsSelected = prevSelected.Contains(d.FullName)
                });
            }

            foreach (var f in dirInfo.GetFiles().OrderBy(x => x.Name))
            {
                if ((f.Attributes & FileAttributes.Hidden) != 0) continue;
                items.Add(new DesktopFileItem
                {
                    FullPath = f.FullName,
                    Name = f.Name,
                    IsDirectory = false,
                    Size = f.Length,
                    CreationTime = f.CreationTime,
                    LastWriteTime = f.LastWriteTime,
                    IsFavorite = _favorites.Contains(f.FullName),
                    IsSelected = prevSelected.Contains(f.FullName)
                });
            }
        }
        catch (Exception ex)
        {
            AppendLog($"Error listing directory: {ex.Message}");
        }

        _currentItems.Clear();
        _currentItems.AddRange(items);

        ApplyFilters();
    }

    private void ApplyFilters()
    {
        var q = _searchBox.Text?.Trim().ToLowerInvariant() ?? "";
        IEnumerable<DesktopFileItem> list = _currentItems;

        if (_filterMode == "Favorites")
            list = list.Where(x => x.IsFavorite);
        else if (_filterMode == "Recent")
        {
            var rank = _recentPaths.Select((p, idx) => (p, idx)).ToDictionary(x => x.p, x => x.idx, StringComparer.OrdinalIgnoreCase);
            list = list.Where(x => rank.ContainsKey(x.FullPath)).OrderBy(x => rank[x.FullPath]);
        }

        if (!string.IsNullOrEmpty(q))
            list = list.Where(x => x.Name.ToLowerInvariant().Contains(q));

        RenderFileRows(list.ToList());
    }

    private void RenderFileRows(List<DesktopFileItem> items)
    {
        _itemsStack.Children.Clear();
        UpdateSelectionUi();

        if (items.Count == 0)
        {
            _emptyPanel.Visibility = Visibility.Visible;
            if (!string.IsNullOrEmpty(_searchBox.Text))
            {
                _emptyTitle.Text = $"No matches for \"{_searchBox.Text}\"";
                _emptySubtitle.Text = "Check your spelling or clear search.";
            }
            else if (_filterMode == "Favorites")
            {
                _emptyTitle.Text = "No favorites yet";
                _emptySubtitle.Text = "Click the ⭐ button on any item to favorite it.";
            }
            else if (_filterMode == "Recent")
            {
                _emptyTitle.Text = "No recent files";
                _emptySubtitle.Text = "Files you open will appear here.";
            }
            else
            {
                _emptyTitle.Text = "Folder is empty";
                _emptySubtitle.Text = "Drag & drop files here, or click '+ Folder' / 'Add Files'.";
            }
            return;
        }

        _emptyPanel.Visibility = Visibility.Collapsed;

        foreach (var item in items)
        {
            var card = new Border
            {
                Background = BgWhite,
                BorderBrush = BorderLight,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(12, 8, 12, 8),
                Margin = new Thickness(0, 0, 0, 6),
                Cursor = Cursors.Hand
            };

            var rowGrid = new Grid();
            rowGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); // Checkbox
            rowGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); // Icon + dot
            rowGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) }); // Name + details
            rowGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); // Action buttons

            // Checkbox for selection
            if (_isSelectMode)
            {
                var cb = new CheckBox
                {
                    IsChecked = item.IsSelected,
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(0, 0, 8, 0)
                };
                cb.Click += (_, _) =>
                {
                    item.IsSelected = cb.IsChecked == true;
                    UpdateSelectionUi();
                };
                Grid.SetColumn(cb, 0);
                rowGrid.Children.Add(cb);
            }

            // Icon + sync dot
            var iconBox = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 10, 0) };
            var syncDot = new Border
            {
                Width = 6, Height = 6,
                CornerRadius = new CornerRadius(3),
                Background = Success,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 6, 0)
            };
            iconBox.Children.Add(syncDot);

            var iconTxt = new TextBlock
            {
                Text = item.IsDirectory ? "📁" : GetFileIcon(item.Name),
                FontSize = 16,
                VerticalAlignment = VerticalAlignment.Center
            };
            iconBox.Children.Add(iconTxt);
            Grid.SetColumn(iconBox, 1);
            rowGrid.Children.Add(iconBox);

            // Name + info
            var nameStack = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            var nameBox = new StackPanel { Orientation = Orientation.Horizontal };
            nameBox.Children.Add(new TextBlock
            {
                Text = item.Name,
                FontWeight = FontWeights.SemiBold,
                FontSize = 12,
                Foreground = TextDark,
                TextTrimming = TextTrimming.CharacterEllipsis
            });
            if (item.IsFavorite)
            {
                nameBox.Children.Add(new TextBlock { Text = " ⭐", FontSize = 11, Foreground = Amber });
            }
            nameStack.Children.Add(nameBox);

            var metaText = item.IsDirectory
                ? $"Folder · {item.LastWriteTime.ToLocalTime():g}"
                : $"{DesktopDialogs.FormatSize(item.Size)} · {item.LastWriteTime.ToLocalTime():g}";
            nameStack.Children.Add(new TextBlock { Text = metaText, FontSize = 10, Foreground = TextMuted, Margin = new Thickness(0, 2, 0, 0) });
            Grid.SetColumn(nameStack, 2);
            rowGrid.Children.Add(nameStack);

            // Actions: Star, Move, Rename, Delete, Details
            var actBox = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };

            var starBtn = CreateIconBtn(item.IsFavorite ? "⭐" : "☆", () => ToggleFavorite(item), item.IsFavorite ? Amber : TextMuted);
            var moveBtn = CreateIconBtn("📦", () => MoveItem(item), TextMuted);
            var detailsBtn = CreateIconBtn("ℹ", () => ShowItemDetails(item), TextMuted);
            var renameBtn = CreateIconBtn("✏", () => RenameItem(item), TextMuted);
            var deleteBtn = CreateIconBtn("🗑", () => DeleteItem(item), Danger);

            actBox.Children.Add(starBtn);
            actBox.Children.Add(moveBtn);
            actBox.Children.Add(detailsBtn);
            actBox.Children.Add(renameBtn);
            actBox.Children.Add(deleteBtn);

            Grid.SetColumn(actBox, 3);
            rowGrid.Children.Add(actBox);

            card.Child = rowGrid;

            // Double click opens folder or launches file
            card.MouseDown += (_, e) =>
            {
                if (e.ClickCount == 2)
                {
                    OpenItem(item);
                }
                else if (_isSelectMode && e.LeftButton == MouseButtonState.Pressed)
                {
                    item.IsSelected = !item.IsSelected;
                    RefreshFileList(keepSelection: true);
                }
            };

            _itemsStack.Children.Add(card);
        }
    }

    private void OpenItem(DesktopFileItem item)
    {
        if (item.IsDirectory)
        {
            _currentDirectory = item.FullPath;
            RefreshFileList();
        }
        else
        {
            try
            {
                PushRecent(item.FullPath);
                Process.Start(new ProcessStartInfo(item.FullPath) { UseShellExecute = true });
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Unable to open file: {ex.Message}", "Open File", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }

    private void NavigateBack()
    {
        var root = _state.LocalRoot;
        if (string.IsNullOrWhiteSpace(root)) return;

        var parent = Directory.GetParent(_currentDirectory);
        if (parent != null && parent.FullName.StartsWith(root, StringComparison.OrdinalIgnoreCase))
        {
            _currentDirectory = parent.FullName;
            RefreshFileList();
        }
        else
        {
            _currentDirectory = root;
            RefreshFileList();
        }
    }

    private void PromptNewFolder()
    {
        var name = DesktopDialogs.Prompt(Window.GetWindow(this), "New Folder", "Enter folder name:");
        if (string.IsNullOrWhiteSpace(name)) return;

        try
        {
            var target = Path.Combine(_currentDirectory, name);
            if (!Directory.Exists(target))
            {
                Directory.CreateDirectory(target);
                AppendLog($"Created folder: {name}");
                RefreshFileList();
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Could not create folder: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void PromptAddFiles()
    {
        var dlg = new OpenFileDialog
        {
            Multiselect = true,
            Title = "Select files to add into NexSync"
        };
        if (dlg.ShowDialog() == true)
        {
            int added = 0;
            foreach (var src in dlg.FileNames)
            {
                try
                {
                    var dest = Path.Combine(_currentDirectory, Path.GetFileName(src));
                    File.Copy(src, dest, overwrite: true);
                    added++;
                }
                catch { }
            }
            AppendLog($"Added {added} files into {_pathBadge.Text}.");
            RefreshFileList();
        }
    }

    private void OpenCurrentInExplorer()
    {
        var target = Directory.Exists(_currentDirectory) ? _currentDirectory : _state.LocalRoot;
        if (!string.IsNullOrWhiteSpace(target) && Directory.Exists(target))
        {
            try { Process.Start(new ProcessStartInfo("explorer.exe", $"\"{target}\"") { UseShellExecute = true }); }
            catch (Exception ex) { AppendLog("Open folder failed: " + ex.Message); }
        }
    }

    private void ToggleFavorite(DesktopFileItem item)
    {
        item.IsFavorite = !item.IsFavorite;
        if (item.IsFavorite) _favorites.Add(item.FullPath);
        else _favorites.Remove(item.FullPath);
        SaveLocalPreferences();
        RefreshFileList(keepSelection: true);
    }

    private void MoveItem(DesktopFileItem item)
    {
        var root = _state.LocalRoot;
        if (string.IsNullOrWhiteSpace(root)) return;

        var destFolder = DesktopDialogs.SelectDestinationFolder(Window.GetWindow(this), root, item.FullPath);
        if (string.IsNullOrWhiteSpace(destFolder)) return;

        try
        {
            var destPath = Path.Combine(destFolder, item.Name);
            if (item.IsDirectory)
                Directory.Move(item.FullPath, destPath);
            else
                File.Move(item.FullPath, destPath, overwrite: true);

            AppendLog($"Moved '{item.Name}' to '{Path.GetRelativePath(root, destFolder)}'.");
            RefreshFileList();
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Could not move item: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void ShowItemDetails(DesktopFileItem item)
    {
        DesktopDialogs.ShowDetails(Window.GetWindow(this),
            item.Name, item.FullPath, item.IsDirectory, item.Size,
            item.CreationTime, item.LastWriteTime, item.IsFavorite);
    }

    private void RenameItem(DesktopFileItem item)
    {
        var newName = DesktopDialogs.Prompt(Window.GetWindow(this), "Rename", "Enter new name:", item.Name);
        if (string.IsNullOrWhiteSpace(newName) || newName.Equals(item.Name, StringComparison.OrdinalIgnoreCase))
            return;

        try
        {
            var dir = Path.GetDirectoryName(item.FullPath)!;
            var newPath = Path.Combine(dir, newName);

            if (item.IsDirectory)
                Directory.Move(item.FullPath, newPath);
            else
                File.Move(item.FullPath, newPath);

            if (_favorites.Contains(item.FullPath))
            {
                _favorites.Remove(item.FullPath);
                _favorites.Add(newPath);
                SaveLocalPreferences();
            }

            AppendLog($"Renamed '{item.Name}' to '{newName}'.");
            RefreshFileList();
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Rename failed: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void DeleteItem(DesktopFileItem item)
    {
        var type = item.IsDirectory ? "folder and all its contents" : "file";
        var res = MessageBox.Show($"Are you sure you want to delete this {type}?", "Confirm Delete", MessageBoxButton.YesNo, MessageBoxImage.Warning);
        if (res != MessageBoxResult.Yes) return;

        try
        {
            if (item.IsDirectory)
                Directory.Delete(item.FullPath, true);
            else
                File.Delete(item.FullPath);

            _favorites.Remove(item.FullPath);
            SaveLocalPreferences();
            AppendLog($"Deleted '{item.Name}'.");
            RefreshFileList();
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Delete failed: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    // --- Multi-select & Bulk delete ---
    private void ToggleSelectMode(bool forceOff = false)
    {
        if (forceOff) _isSelectMode = false;
        else _isSelectMode = !_isSelectMode;

        _selectModeToggleBtn.Content = _isSelectMode ? "Done" : "Select";
        _selectionBar.Visibility = _isSelectMode ? Visibility.Visible : Visibility.Collapsed;

        if (!_isSelectMode)
        {
            foreach (var it in _currentItems) it.IsSelected = false;
        }

        RefreshFileList(keepSelection: true);
    }

    private void UpdateSelectionUi()
    {
        var count = _currentItems.Count(x => x.IsSelected);
        _selectionCountLabel.Text = $"{count} selected";
    }

    private void BulkDeleteSelected()
    {
        var selected = _currentItems.Where(x => x.IsSelected).ToList();
        if (selected.Count == 0) return;

        var res = MessageBox.Show($"Delete {selected.Count} selected items permanently?", "Delete Selected", MessageBoxButton.YesNo, MessageBoxImage.Warning);
        if (res != MessageBoxResult.Yes) return;

        int deleted = 0;
        foreach (var item in selected)
        {
            try
            {
                if (item.IsDirectory) Directory.Delete(item.FullPath, true);
                else File.Delete(item.FullPath);
                _favorites.Remove(item.FullPath);
                deleted++;
            }
            catch { }
        }

        SaveLocalPreferences();
        AppendLog($"Deleted {deleted} items in bulk.");
        ToggleSelectMode(forceOff: true);
    }

    // --- Drag and Drop ---
    private void OnDragOver(object sender, DragEventArgs e)
    {
        if (e.Data.GetDataPresent(DataFormats.FileDrop))
        {
            e.Effects = DragDropEffects.Copy;
            e.Handled = true;
        }
        else
        {
            e.Effects = DragDropEffects.None;
        }
    }

    private void OnFilesDropped(object sender, DragEventArgs e)
    {
        if (e.Data.GetDataPresent(DataFormats.FileDrop))
        {
            var files = (string[])e.Data.GetData(DataFormats.FileDrop);
            if (files != null && files.Length > 0)
            {
                int copied = 0;
                foreach (var file in files)
                {
                    try
                    {
                        var name = Path.GetFileName(file);
                        var dest = Path.Combine(_currentDirectory, name);
                        if (Directory.Exists(file))
                            CopyDirectoryRecursively(file, dest);
                        else
                            File.Copy(file, dest, overwrite: true);
                        copied++;
                    }
                    catch { }
                }
                AppendLog($"Imported {copied} files/folders via drag-and-drop.");
                RefreshFileList();
            }
        }
    }

    private static void CopyDirectoryRecursively(string sourceDir, string targetDir)
    {
        Directory.CreateDirectory(targetDir);
        foreach (var file in Directory.GetFiles(sourceDir))
            File.Copy(file, Path.Combine(targetDir, Path.GetFileName(file)), true);
        foreach (var sub in Directory.GetDirectories(sourceDir))
            CopyDirectoryRecursively(sub, Path.Combine(targetDir, Path.GetFileName(sub)));
    }

    // --- Persistence & Recents ---
    private void LoadLocalPreferences()
    {
        try
        {
            if (File.Exists(_favFile))
            {
                var json = File.ReadAllText(_favFile);
                var arr = JsonSerializer.Deserialize<string[]>(json);
                if (arr != null)
                {
                    foreach (var s in arr) _favorites.Add(s);
                }
            }
        }
        catch { }

        try
        {
            if (File.Exists(_recentFile))
            {
                var json = File.ReadAllText(_recentFile);
                var arr = JsonSerializer.Deserialize<string[]>(json);
                if (arr != null)
                {
                    _recentPaths.AddRange(arr.Take(25));
                }
            }
        }
        catch { }
    }

    private void SaveLocalPreferences()
    {
        try
        {
            File.WriteAllText(_favFile, JsonSerializer.Serialize(_favorites.ToArray()));
            File.WriteAllText(_recentFile, JsonSerializer.Serialize(_recentPaths.Take(25).ToArray()));
        }
        catch { }
    }

    private void PushRecent(string path)
    {
        try
        {
            _recentPaths.RemoveAll(x => x.Equals(path, StringComparison.OrdinalIgnoreCase));
            _recentPaths.Insert(0, path);
            if (_recentPaths.Count > 25) _recentPaths.RemoveRange(25, _recentPaths.Count - 25);
            SaveLocalPreferences();
        }
        catch { }
    }

    // --- Helpers ---
    private static string GetFileIcon(string name)
    {
        var ext = Path.GetExtension(name).ToLowerInvariant();
        return ext switch
        {
            ".png" or ".jpg" or ".jpeg" or ".gif" or ".webp" or ".svg" => "🖼️",
            ".pdf" => "📕",
            ".mp3" or ".wav" or ".flac" or ".m4a" => "🎵",
            ".mp4" or ".mkv" or ".mov" or ".avi" => "🎬",
            ".zip" or ".tar" or ".gz" or ".7z" or ".rar" => "📦",
            ".json" or ".js" or ".ts" or ".cs" or ".html" or ".css" or ".py" => "📜",
            ".txt" or ".md" or ".log" => "📝",
            _ => "📄"
        };
    }

    private static Button CreateSmallBtn(string text, Action onClick, bool isDanger = false)
    {
        var btn = new Button
        {
            Content = text,
            Background = isDanger ? Br("#FEE2E2") : BgWhite,
            Foreground = isDanger ? Danger : TextDark,
            BorderBrush = isDanger ? Br("#FECACA") : BorderLight,
            BorderThickness = new Thickness(1),
            FontWeight = FontWeights.SemiBold,
            FontSize = 11,
            Padding = new Thickness(10, 4, 10, 4),
            Margin = new Thickness(4, 0, 0, 0),
            Cursor = Cursors.Hand
        };
        btn.Click += (_, _) => onClick();
        return btn;
    }

    private static Button CreateIconBtn(string text, Action onClick, SolidColorBrush color)
    {
        var btn = new Button
        {
            Content = text,
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            Foreground = color,
            FontSize = 12,
            Padding = new Thickness(4, 2, 4, 2),
            Margin = new Thickness(2, 0, 0, 0),
            Cursor = Cursors.Hand
        };
        btn.Click += (s, e) =>
        {
            e.Handled = true;
            onClick();
        };
        return btn;
    }

    private void RefreshSyncState()
    {
        if (_state.SyncRequested)
        {
            _statusLabel.Text = "Syncing…";
            _statusLabel.Foreground = TextDark;
            _dot.Background = Amber;
            _statusPath.Text = string.IsNullOrWhiteSpace(_state.LocalRoot) ? "Sync pending" : _state.LocalRoot!;
            _lastSync.Text = "Sync in progress…";
        }
        else if (!string.IsNullOrWhiteSpace(_state.LocalRoot))
        {
            _statusLabel.Text = "All synced";
            _statusLabel.Foreground = Success;
            _dot.Background = Success;
            _statusPath.Text = _state.LocalRoot!;
            _lastSync.Text = "Last sync just now · local folder: " + _state.LocalRoot;
        }
        else
        {
            _statusLabel.Text = "Idle";
            _statusLabel.Foreground = TextMuted;
            _dot.Background = TextMuted;
            _statusPath.Text = "No local folder configured";
            _lastSync.Text = "Configure a folder to start syncing";
        }
    }

    public void AppendLog(string message)
    {
        var line = $"[{DateTime.Now:HH:mm:ss}] {message}";
        if (Dispatcher.CheckAccess())
        {
            _log.AppendText(line + Environment.NewLine);
            _log.ScrollToEnd();
        }
        else
        {
            Dispatcher.Invoke(() =>
            {
                _log.AppendText(line + Environment.NewLine);
                _log.ScrollToEnd();
            });
        }
    }
}
