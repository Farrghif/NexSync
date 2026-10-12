using System;
using System.Windows.Controls;
using NexSync.Desktop.Sync;

namespace NexSync.Desktop.Views;

public sealed class SetupView : System.Windows.Controls.UserControl
{
    public SetupView(AppState state, Func<string, SetupService> setups)
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
        var status = new System.Windows.Controls.TextBlock { Margin = new System.Windows.Thickness(0, 8, 0, 0) };
        var empty = new System.Windows.Controls.Button { Content = "Start with empty folder (recommended)", Margin = new System.Windows.Thickness(0, 8, 0, 0) };
        empty.Click += (_, _) =>
        {
            if (string.IsNullOrWhiteSpace(pathBox.Text))
            {
                status.Text = "Pick a folder first.";
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
                status.Text = ex.Message;
            }
        };
        var import = new System.Windows.Controls.Button { Content = "Import existing files", Margin = new System.Windows.Thickness(0, 8, 0, 0) };
        import.Click += (_, _) =>
        {
            if (string.IsNullOrWhiteSpace(pathBox.Text))
            {
                status.Text = "Pick a folder first.";
                return;
            }
            try
            {
                using var setup = setups.Invoke(pathBox.Text);
                setup.ApplyImportSetup();
                state.SetLocalRoot(pathBox.Text);
                state.GoTo(AppScreen.Status);
            }
            catch (Exception ex)
            {
                status.Text = ex.Message;
            }
        };
        panel.Children.Add(pathBox);
        panel.Children.Add(browse);
        panel.Children.Add(empty);
        panel.Children.Add(import);
        panel.Children.Add(status);
        Content = panel;
    }
}
