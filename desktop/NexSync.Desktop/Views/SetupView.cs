using System;
using System.Windows.Controls;

namespace NexSync.Desktop.Views;

public sealed class SetupView : System.Windows.Controls.UserControl
{
    public SetupView(AppState state)
    {
        var panel = new System.Windows.Controls.StackPanel { Margin = new System.Windows.Thickness(24) };
        panel.Children.Add(new System.Windows.Controls.Label { Content = "Choose your NexSync folder" });
        var pathBox = new System.Windows.Controls.TextBox { IsReadOnly = true, Margin = new System.Windows.Thickness(0, 4, 0, 4) };
        var browse = new System.Windows.Controls.Button { Content = "Browse…", Margin = new System.Windows.Thickness(0, 4, 0, 4) };
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
        var start = new System.Windows.Controls.Button { Content = "Start syncing (stub — Task 11+)", Margin = new System.Windows.Thickness(0, 8, 0, 0) };
        start.Click += (_, _) =>
        {
            if (!string.IsNullOrWhiteSpace(pathBox.Text))
                state.SetLocalRoot(pathBox.Text);
            state.GoTo(AppScreen.Status);
        };
        panel.Children.Add(pathBox);
        panel.Children.Add(browse);
        panel.Children.Add(start);
        Content = panel;
    }
}


