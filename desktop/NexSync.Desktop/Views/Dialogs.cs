using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace NexSync.Desktop.Views;

public static class DesktopDialogs
{
    private static SolidColorBrush Br(string hex) => new((Color)ColorConverter.ConvertFromString(hex));
    private static readonly SolidColorBrush BgWhite = Br("#FFFFFF");
    private static readonly SolidColorBrush BorderLight = Br("#E6E8EF");
    private static readonly SolidColorBrush Brand = Br("#5B5BD6");
    private static readonly SolidColorBrush TextDark = Br("#14161D");
    private static readonly SolidColorBrush TextMuted = Br("#8B90A0");

    public static string? Prompt(Window? owner, string title, string prompt, string initialValue = "")
    {
        var win = new Window
        {
            Title = title,
            Width = 420,
            Height = 220,
            WindowStartupLocation = WindowStartupLocation.CenterScreen,
            ResizeMode = ResizeMode.NoResize,
            Background = BgWhite,
            Owner = owner
        };

        var stack = new StackPanel { Margin = new Thickness(24) };
        stack.Children.Add(new TextBlock
        {
            Text = prompt,
            FontWeight = FontWeights.SemiBold,
            FontSize = 13,
            Foreground = TextDark,
            Margin = new Thickness(0, 0, 0, 12)
        });

        var inputBorder = new Border
        {
            Background = BgWhite,
            BorderBrush = BorderLight,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(10, 8, 10, 8),
            Margin = new Thickness(0, 0, 0, 16)
        };
        var txt = new TextBox
        {
            Text = initialValue,
            BorderThickness = new Thickness(0),
            Background = Brushes.Transparent,
            FontSize = 13,
            Foreground = TextDark
        };
        txt.SelectAll();
        inputBorder.Child = txt;
        stack.Children.Add(inputBorder);

        var btnGrid = new Grid();
        btnGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        btnGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        var cancelBtn = new Button
        {
            Content = "Cancel",
            Background = Brushes.Transparent,
            Foreground = TextMuted,
            FontWeight = FontWeights.SemiBold,
            FontSize = 12,
            Padding = new Thickness(12, 8, 12, 8),
            Margin = new Thickness(0, 0, 6, 0)
        };
        cancelBtn.Click += (_, _) => win.DialogResult = false;
        Grid.SetColumn(cancelBtn, 0);
        btnGrid.Children.Add(cancelBtn);

        var okBtn = new Button
        {
            Content = "OK",
            Background = Brand,
            Foreground = Brushes.White,
            FontWeight = FontWeights.SemiBold,
            FontSize = 12,
            Padding = new Thickness(12, 8, 12, 8),
            Margin = new Thickness(6, 0, 0, 0)
        };
        okBtn.Click += (_, _) => win.DialogResult = true;
        Grid.SetColumn(okBtn, 1);
        btnGrid.Children.Add(okBtn);

        stack.Children.Add(btnGrid);
        win.Content = stack;

        txt.KeyDown += (_, e) =>
        {
            if (e.Key == System.Windows.Input.Key.Enter) win.DialogResult = true;
            else if (e.Key == System.Windows.Input.Key.Escape) win.DialogResult = false;
        };

        win.Loaded += (_, _) => txt.Focus();

        var res = win.ShowDialog();
        return res == true ? txt.Text.Trim() : null;
    }

    public static string? SelectDestinationFolder(Window? owner, string rootDir, string currentItemPath)
    {
        var win = new Window
        {
            Title = "Move to Folder",
            Width = 440,
            Height = 360,
            WindowStartupLocation = WindowStartupLocation.CenterScreen,
            Background = BgWhite,
            Owner = owner
        };

        var stack = new Grid { Margin = new Thickness(20) };
        stack.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        stack.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        stack.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var header = new TextBlock
        {
            Text = "Select destination folder:",
            FontWeight = FontWeights.SemiBold,
            FontSize = 13,
            Foreground = TextDark,
            Margin = new Thickness(0, 0, 0, 10)
        };
        Grid.SetRow(header, 0);
        stack.Children.Add(header);

        var list = new ListBox
        {
            BorderBrush = BorderLight,
            BorderThickness = new Thickness(1),
            Background = Brushes.Transparent,
            FontSize = 12,
            Foreground = TextDark,
            Margin = new Thickness(0, 0, 0, 14)
        };

        var folders = new List<(string Display, string Path)>
        {
            ("📁 Root (Top-level)", rootDir)
        };

        try
        {
            var subDirs = Directory.GetDirectories(rootDir, "*", SearchOption.AllDirectories);
            foreach (var d in subDirs)
            {
                if (d.Equals(currentItemPath, StringComparison.OrdinalIgnoreCase) ||
                    d.StartsWith(currentItemPath + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                    continue;

                var rel = Path.GetRelativePath(rootDir, d);
                folders.Add(($"📁 {rel}", d));
            }
        }
        catch { }

        foreach (var f in folders)
            list.Items.Add(f.Display);

        if (list.Items.Count > 0) list.SelectedIndex = 0;

        Grid.SetRow(list, 1);
        stack.Children.Add(list);

        var btnGrid = new Grid();
        btnGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        btnGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        var cancelBtn = new Button
        {
            Content = "Cancel",
            Background = Brushes.Transparent,
            Foreground = TextMuted,
            FontWeight = FontWeights.SemiBold,
            FontSize = 12,
            Padding = new Thickness(12, 8, 12, 8),
            Margin = new Thickness(0, 0, 6, 0)
        };
        cancelBtn.Click += (_, _) => win.DialogResult = false;
        Grid.SetColumn(cancelBtn, 0);
        btnGrid.Children.Add(cancelBtn);

        var moveBtn = new Button
        {
            Content = "Move Here",
            Background = Brand,
            Foreground = Brushes.White,
            FontWeight = FontWeights.SemiBold,
            FontSize = 12,
            Padding = new Thickness(12, 8, 12, 8),
            Margin = new Thickness(6, 0, 0, 0)
        };
        moveBtn.Click += (_, _) => win.DialogResult = true;
        Grid.SetColumn(moveBtn, 1);
        btnGrid.Children.Add(moveBtn);

        Grid.SetRow(btnGrid, 2);
        stack.Children.Add(btnGrid);

        win.Content = stack;
        var res = win.ShowDialog();
        if (res == true && list.SelectedIndex >= 0 && list.SelectedIndex < folders.Count)
        {
            return folders[list.SelectedIndex].Path;
        }

        return null;
    }

    public static void ShowDetails(Window? owner, string name, string path, bool isDir, long size, DateTime created, DateTime modified, bool isFavorite)
    {
        var win = new Window
        {
            Title = "Item Details",
            Width = 460,
            Height = 340,
            WindowStartupLocation = WindowStartupLocation.CenterScreen,
            ResizeMode = ResizeMode.NoResize,
            Background = BgWhite,
            Owner = owner
        };

        var stack = new StackPanel { Margin = new Thickness(24) };

        void AddRow(string label, string val)
        {
            var grid = new Grid { Margin = new Thickness(0, 0, 0, 8) };
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(110) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            grid.Children.Add(new TextBlock { Text = label, Foreground = TextMuted, FontSize = 12 });
            var valBlock = new TextBlock { Text = val, Foreground = TextDark, FontWeight = FontWeights.SemiBold, FontSize = 12, TextWrapping = TextWrapping.Wrap };
            Grid.SetColumn(valBlock, 1);
            grid.Children.Add(valBlock);
            stack.Children.Add(grid);
        }

        AddRow("Name", name);
        AddRow("Type", isDir ? "Folder" : (Path.GetExtension(name) + " File"));
        AddRow("Size", isDir ? "—" : FormatSize(size));
        AddRow("Path", path);
        AddRow("Modified", modified.ToLocalTime().ToString("g"));
        AddRow("Created", created.ToLocalTime().ToString("g"));
        AddRow("Favorite", isFavorite ? "Yes ⭐" : "No");

        var closeBtn = new Button
        {
            Content = "Close",
            Background = Brand,
            Foreground = Brushes.White,
            FontWeight = FontWeights.SemiBold,
            FontSize = 12,
            Padding = new Thickness(14, 8, 14, 8),
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 16, 0, 0)
        };
        closeBtn.Click += (_, _) => win.Close();
        stack.Children.Add(closeBtn);

        win.Content = stack;
        win.ShowDialog();
    }

    public static string FormatSize(long bytes)
    {
        if (bytes < 1024) return $"{bytes} B";
        if (bytes < 1024 * 1024) return $"{(bytes / 1024.0):F1} KB";
        if (bytes < 1024 * 1024 * 1024) return $"{(bytes / (1024.0 * 1024)):F1} MB";
        return $"{(bytes / (1024.0 * 1024 * 1024)):F2} GB";
    }
}
