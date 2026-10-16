using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using NexSync.Desktop.Sync;

namespace NexSync.Desktop.Views;

public sealed class SetupView : UserControl
{
    private static SolidColorBrush Br(string hex) => new((Color)ColorConverter.ConvertFromString(hex));
    private static readonly SolidColorBrush BgLight = Br("#F6F7FB");
    private static readonly SolidColorBrush BgWhite = Br("#FFFFFF");
    private static readonly SolidColorBrush BorderLight = Br("#E6E8EF");
    private static readonly SolidColorBrush BorderInput = Br("#D5D8E3");
    private static readonly SolidColorBrush Brand = Br("#5B5BD6");
    private static readonly SolidColorBrush TextDark = Br("#14161D");
    private static readonly SolidColorBrush TextMuted = Br("#8B90A0");
    private static readonly SolidColorBrush Danger = Br("#C62B3C");
    private static readonly SolidColorBrush Success = Br("#15803D");

    public SetupView(AppState state, Func<string, SetupService> setups)
    {
        Background = BgLight;
        FontFamily = new FontFamily("Segoe UI");

        var outer = new Grid { Background = BgLight };
        var scroll = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
        var card = new Border
        {
            Background = BgWhite,
            BorderBrush = BorderLight,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(16),
            Padding = new Thickness(28),
            MaxWidth = 480,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(24)
        };

        var stack = new StackPanel();

        stack.Children.Add(new TextBlock
        {
            Text = "Choose your NexSync folder",
            FontSize = 16,
            FontWeight = FontWeights.Bold,
            Foreground = TextDark,
            Margin = new Thickness(0, 0, 0, 6)
        });
        stack.Children.Add(new TextBlock
        {
            Text = "Where NexSync keeps your synced files on this PC. You can change it later.",
            FontSize = 12,
            Foreground = TextMuted,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 20)
        });

        // Path row
        var pathBorder = new Border
        {
            BorderBrush = BorderLight,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(12),
            Background = BgWhite,
            Padding = new Thickness(12),
            Margin = new Thickness(0, 0, 0, 16)
        };
        var pathGrid = new Grid();
        pathGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        pathGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var pathBox = new TextBox
        {
            IsReadOnly = true,
            BorderThickness = new Thickness(0),
            Background = Brushes.Transparent,
            Foreground = TextDark,
            FontSize = 12,
            VerticalAlignment = VerticalAlignment.Center,
            Padding = new Thickness(0),
            ToolTip = "Selected folder path"
        };
        // placeholder overlay TextBlock (shown when pathBox empty)
        var pathOverlay = new TextBlock
        {
            Text = "No folder selected",
            Foreground = TextMuted,
            FontSize = 12,
            VerticalAlignment = VerticalAlignment.Center,
            IsHitTestVisible = false,
            Margin = new Thickness(1, 0, 0, 0)
        };
        var pathInnerGrid = new Grid();
        pathInnerGrid.Children.Add(pathBox);
        pathInnerGrid.Children.Add(pathOverlay);
        pathBox.TextChanged += (_, _) => pathOverlay.Visibility = string.IsNullOrWhiteSpace(pathBox.Text) ? Visibility.Visible : Visibility.Collapsed;

        var browseBorder = new Border
        {
            BorderBrush = Brand,
            BorderThickness = new Thickness(1.4),
            CornerRadius = new CornerRadius(8),
            Background = BgWhite,
            Margin = new Thickness(12, 0, 0, 0)
        };
        var browse = new Button
        {
            Content = "Browse…",
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            Foreground = Brand,
            FontWeight = FontWeights.SemiBold,
            FontSize = 12,
            Padding = new Thickness(14, 7, 14, 7),
            Cursor = System.Windows.Input.Cursors.Hand
        };
        browseBorder.Child = browse;
        browse.Click += (_, _) =>
        {
            var dlg = new System.Windows.Forms.FolderBrowserDialog
            {
                Description = "Choose the local NexSync folder",
                UseDescriptionForTitle = true
            };
            if (dlg.ShowDialog() == System.Windows.Forms.DialogResult.OK)
                pathBox.Text = dlg.SelectedPath;
        };
        Grid.SetColumn(pathInnerGrid, 0);
        Grid.SetColumn(browseBorder, 1);
        pathGrid.Children.Add(pathInnerGrid);
        pathGrid.Children.Add(browseBorder);
        pathBorder.Child = pathGrid;
        stack.Children.Add(pathBorder);

        var status = new TextBlock
        {
            FontSize = 12,
            TextWrapping = TextWrapping.Wrap,
            Visibility = Visibility.Collapsed,
            Margin = new Thickness(0, 0, 0, 12),
            Foreground = Danger
        };
        void SetStatus(string msg, bool isSuccess = false)
        {
            if (string.IsNullOrWhiteSpace(msg))
            {
                status.Visibility = Visibility.Collapsed;
                status.Text = string.Empty;
            }
            else
            {
                status.Text = msg;
                status.Foreground = isSuccess ? Success : Danger;
                status.Visibility = Visibility.Visible;
            }
        }
        // status below path, above cards
        stack.Children.Add(status);

        // Card 1: Start empty
        var card1 = CreateChoiceCard(
            icon: "◇",
            title: "Start empty (recommended)",
            desc: "Create a fresh folder. Good for new setups.",
            buttonText: "Start empty",
            isPrimary: true,
            onClick: () =>
            {
                if (string.IsNullOrWhiteSpace(pathBox.Text))
                {
                    SetStatus("Pick a folder first.");
                    return;
                }
                try
                {
                    using var setup = setups.Invoke(pathBox.Text);
                    setup.ApplyEmptySetup();
                    state.SetLocalRoot(pathBox.Text);
                    state.GoTo(AppScreen.Status);
                }
                catch (Exception ex)
                {
                    SetStatus(ex.Message);
                }
            });

        // Card 2: Import
        var card2 = CreateChoiceCard(
            icon: "⬆",
            title: "Import existing files",
            desc: "Scan this folder and queue everything to sync. Duplicates return 409.",
            buttonText: "Import files",
            isPrimary: false,
            onClick: () =>
            {
                if (string.IsNullOrWhiteSpace(pathBox.Text))
                {
                    SetStatus("Pick a folder first.");
                    return;
                }
                try
                {
                    using var setup = setups.Invoke(pathBox.Text);
                    setup.ApplyImportSetup();
                    state.SetLocalRoot(pathBox.Text);
                    SetStatus("Import queued — syncing on next sync.", isSuccess: true);
                    state.GoTo(AppScreen.Status);
                }
                catch (Exception ex)
                {
                    SetStatus(ex.Message);
                }
            });

        stack.Children.Add(card1);
        stack.Children.Add(card2);

        // hint footer
        stack.Children.Add(new TextBlock
        {
            Text = "Tip: pick an empty folder if you're setting up a new device. Import will hash every file and queue them for sync.",
            FontSize = 11,
            Foreground = TextMuted,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 16, 0, 0)
        });

        card.Child = stack;
        scroll.Content = card;
        outer.Children.Add(scroll);
        Content = outer;

        // initial placeholder state
        pathOverlay.Visibility = Visibility.Visible;
    }

    private static Border CreateChoiceCard(string icon, string title, string desc, string buttonText, bool isPrimary, Action onClick)
    {
        var brBrand = (SolidColorBrush)new BrushConverter().ConvertFromString("#5B5BD6");
        var brLight = (SolidColorBrush)new BrushConverter().ConvertFromString("#E6E8EF");
        var brWhite = Brushes.White;
        var textDark = (SolidColorBrush)new BrushConverter().ConvertFromString("#14161D");
        var textMuted = (SolidColorBrush)new BrushConverter().ConvertFromString("#8B90A0");
        var bgLight = (SolidColorBrush)new BrushConverter().ConvertFromString("#F6F7FB");

        var outer = new Border
        {
            BorderBrush = brLight,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(12),
            Background = brWhite,
            Padding = new Thickness(14),
            Margin = new Thickness(0, 0, 0, 10)
        };
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var iconBox = new Border
        {
            Width = 36, Height = 36,
            CornerRadius = new CornerRadius(8),
            Background = isPrimary ? brBrand : bgLight,
            BorderBrush = isPrimary ? brBrand : brLight,
            BorderThickness = new Thickness(1),
            VerticalAlignment = VerticalAlignment.Top,
            Margin = new Thickness(0, 0, 12, 0)
        };
        iconBox.Child = new TextBlock
        {
            Text = icon,
            Foreground = isPrimary ? Brushes.White : textDark,
            FontSize = 14,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };
        Grid.SetColumn(iconBox, 0);
        grid.Children.Add(iconBox);

        var textStack = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        textStack.Children.Add(new TextBlock { Text = title, FontSize = 13, FontWeight = FontWeights.SemiBold, Foreground = textDark, TextWrapping = TextWrapping.Wrap });
        textStack.Children.Add(new TextBlock { Text = desc, FontSize = 11, Foreground = textMuted, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 4, 0, 0) });
        Grid.SetColumn(textStack, 1);
        grid.Children.Add(textStack);

        var btnBorder = new Border
        {
            CornerRadius = new CornerRadius(8),
            Background = isPrimary ? brBrand : brWhite,
            BorderBrush = brBrand,
            BorderThickness = new Thickness(1),
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(12, 0, 0, 0)
        };
        var btn = new Button
        {
            Content = buttonText,
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            Foreground = isPrimary ? Brushes.White : brBrand,
            FontWeight = FontWeights.SemiBold,
            FontSize = 12,
            Padding = new Thickness(14, 7, 14, 7),
            Cursor = System.Windows.Input.Cursors.Hand
        };
        btn.Click += (_, _) => onClick();
        btnBorder.Child = btn;
        Grid.SetColumn(btnBorder, 2);
        grid.Children.Add(btnBorder);

        // responsive: if narrow, buttons wrap — but keep simple grid for now
        outer.Child = grid;
        return outer;
    }
}


