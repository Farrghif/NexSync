using System;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Input;
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

    private static SolidColorBrush Br(string hex) => new((Color)ColorConverter.ConvertFromString(hex));
    private static readonly SolidColorBrush BgLight = Br("#F6F7FB");
    private static readonly SolidColorBrush BgWhite = Br("#FFFFFF");
    private static readonly SolidColorBrush BorderLight = Br("#E6E8EF");
    private static readonly SolidColorBrush Brand = Br("#5B5BD6");
    private static readonly SolidColorBrush TextDark = Br("#14161D");
    private static readonly SolidColorBrush TextMuted = Br("#8B90A0");
    private static readonly SolidColorBrush Danger = Br("#C62B3C");
    private static readonly SolidColorBrush Success = Br("#15803D");
    private static readonly SolidColorBrush Amber = Br("#CA8A04");
    private static readonly SolidColorBrush SuccessBg = Br("#DCFCE7");
    private static readonly SolidColorBrush AmberBg = Br("#FEF9C3");

    public StatusView(AppState state, DesktopAuthService auth)
    {
        _state = state;
        _auth = auth;
        Background = BgLight;
        FontFamily = new FontFamily("Segoe UI");

        var outer = new Grid { Background = BgLight };
        outer.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        outer.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

        // Header
        var header = new Border
        {
            Background = BgWhite,
            BorderBrush = BorderLight,
            BorderThickness = new Thickness(0, 0, 0, 1),
            Padding = new Thickness(20, 16, 20, 16)
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

        // sync dot hint in header right? keep status card for main sync state

        header.Child = headerGrid;
        Grid.SetRow(header, 0);
        outer.Children.Add(header);

        // Content scroll
        var scroll = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, Padding = new Thickness(20) };
        var stack = new StackPanel { MaxWidth = 520, HorizontalAlignment = HorizontalAlignment.Center };

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

        var btnRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 14, 0, 0) };
        var syncNowBorder = new Border { Background = Brand, CornerRadius = new CornerRadius(8), Margin = new Thickness(0, 0, 8, 0) };
        var syncNow = new Button
        {
            Content = "Sync now",
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            Foreground = Brushes.White,
            FontWeight = FontWeights.SemiBold,
            FontSize = 12,
            Padding = new Thickness(16, 8, 16, 8),
            Cursor = Cursors.Hand
        };
        syncNow.Click += (_, _) =>
        {
            state.RequestSync();
            AppendLog("Sync requested by user.");
            RefreshSyncState();
        };
        syncNowBorder.Child = syncNow;
        btnRow.Children.Add(syncNowBorder);

        var openFolderBorder = new Border { Background = BgWhite, BorderBrush = BorderLight, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(8) };
        var openFolder = new Button
        {
            Content = "Open folder",
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            Foreground = TextDark,
            FontWeight = FontWeights.SemiBold,
            FontSize = 12,
            Padding = new Thickness(16, 8, 16, 8),
            Cursor = Cursors.Hand
        };
        openFolder.Click += (_, _) =>
        {
            var root = state.LocalRoot;
            if (string.IsNullOrWhiteSpace(root) || !Directory.Exists(root))
            {
                AppendLog(string.IsNullOrWhiteSpace(root) ? "No local folder configured." : $"Folder not found: {root}");
                return;
            }
            try
            {
                Process.Start(new ProcessStartInfo(root) { UseShellExecute = true });
                AppendLog($"Opened folder: {root}");
            }
            catch (Exception ex)
            {
                AppendLog("Open folder failed: " + ex.Message);
            }
        };
        openFolderBorder.Child = openFolder;
        btnRow.Children.Add(openFolderBorder);

        Grid.SetRow(btnRow, 2);
        syncGrid.Children.Add(btnRow);

        syncCard.Child = syncGrid;
        stack.Children.Add(syncCard);

        // Activity log label + actions
        var logHeader = new Grid { Margin = new Thickness(0, 0, 0, 8) };
        logHeader.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        logHeader.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        logHeader.Children.Add(new TextBlock { Text = "Activity log", FontSize = 12, FontWeight = FontWeights.SemiBold, Foreground = TextDark, VerticalAlignment = VerticalAlignment.Center });
        var clearLogBtn = new Button
        {
            Content = "Clear",
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            Foreground = TextMuted,
            FontSize = 11,
            Padding = new Thickness(8, 4, 8, 4),
            Cursor = Cursors.Hand
        };
        clearLogBtn.Click += (_, _) => _log.Clear();
        Grid.SetColumn(clearLogBtn, 1);
        logHeader.Children.Add(clearLogBtn);
        stack.Children.Add(logHeader);

        var logBorder = new Border
        {
            Background = BgWhite,
            BorderBrush = BorderLight,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(12),
            Padding = new Thickness(8),
            Margin = new Thickness(0, 0, 0, 16)
        };
        _log = new TextBox
        {
            IsReadOnly = true,
            AcceptsReturn = true,
            TextWrapping = TextWrapping.Wrap,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            Height = 200,
            BorderThickness = new Thickness(0),
            Background = Brushes.Transparent,
            Foreground = TextDark,
            FontFamily = new FontFamily("Consolas"),
            FontSize = 11,
            Padding = new Thickness(4)
        };
        logBorder.Child = _log;
        stack.Children.Add(logBorder);

        // Footer actions
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

        // local path hint below footer
        if (!string.IsNullOrWhiteSpace(state.LocalRoot))
        {
            stack.Children.Add(new TextBlock
            {
                Text = state.LocalRoot!,
                FontSize = 10,
                Foreground = TextMuted,
                TextTrimming = TextTrimming.CharacterEllipsis,
                TextAlignment = TextAlignment.Center,
                Margin = new Thickness(0, 10, 0, 0),
                ToolTip = state.LocalRoot
            });
        }

        scroll.Content = stack;
        Grid.SetRow(scroll, 1);
        outer.Children.Add(scroll);
        Content = outer;

        // Tray / external sync integration
        state.Changed += () =>
        {
            if (state.SyncRequested)
            {
                AppendLog("Sync signal received.");
                state.ClearSyncRequest();
            }
            RefreshSyncState();
        };

        RefreshSyncState();
        AppendLog("Ready.");
        if (!string.IsNullOrWhiteSpace(state.LocalRoot))
            AppendLog($"Local folder: {state.LocalRoot}");
    }

    private void RefreshSyncState()
    {
        // Derive summary from AppState per spec: SyncRequested -> Syncing else All synced + path
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
            _statusPath.Text = "No folder selected — choose one to start syncing.";
            _lastSync.Text = "Waiting for setup.";
        }
    }

    public void AppendLog(string line)
    {
        Dispatcher.InvokeAsync(() =>
        {
            _log.AppendText($"[{DateTime.Now:HH:mm:ss}] {line}{Environment.NewLine}");
            _log.ScrollToEnd();
        });
    }
}
