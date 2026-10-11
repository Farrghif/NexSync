using System;
using System.Windows.Controls;
using NexSync.Desktop.Auth;

namespace NexSync.Desktop.Views;

public sealed class StatusView : System.Windows.Controls.UserControl
{
    private readonly TextBox _log;
    private readonly TextBlock _status;

    public StatusView(AppState state, DesktopAuthService auth)
    {
        var panel = new System.Windows.Controls.StackPanel { Margin = new System.Windows.Thickness(24) };
        var who = auth.CurrentUser is null ? "Not signed in" : $"{auth.CurrentUser.Email} · device {auth.DeviceId?[..8]}…";
        panel.Children.Add(new System.Windows.Controls.TextBlock { Text = who });
        _status = new System.Windows.Controls.TextBlock { Text = "Status: Idle (sync engine lands in Task 14)" };
        var syncNow = new System.Windows.Controls.Button { Content = "Sync Now", Margin = new System.Windows.Thickness(0, 8, 0, 8) };
        syncNow.Click += (_, _) =>
        {
            state.RequestSync();
            AppendLog("Sync requested by user (no-op until Task 14).");
        };
        _log = new System.Windows.Controls.TextBox
        {
            IsReadOnly = true,
            AcceptsReturn = true,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            Height = 220
        };
        state.Changed += () =>
        {
            if (state.SyncRequested)
            {
                AppendLog("Sync signal received (no-op until Task 14).");
                state.ClearSyncRequest();
            }
        };
        var back = new System.Windows.Controls.Button { Content = "Change folder", Margin = new System.Windows.Thickness(0, 8, 0, 0) };
        back.Click += (_, _) => state.GoTo(AppScreen.Setup);
        var signOut = new System.Windows.Controls.Button { Content = "Sign out", Margin = new System.Windows.Thickness(0, 8, 0, 0) };
        signOut.Click += async (_, _) =>
        {
            signOut.IsEnabled = false;
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
            }
        };
        panel.Children.Add(_status);
        panel.Children.Add(syncNow);
        panel.Children.Add(new System.Windows.Controls.Label { Content = "Log" });
        panel.Children.Add(_log);
        panel.Children.Add(back);
        panel.Children.Add(signOut);
        Content = panel;
    }

    public void AppendLog(string line)
    {
        _log.AppendText($"[{DateTime.Now:HH:mm:ss}] {line}{Environment.NewLine}");
    }
}


