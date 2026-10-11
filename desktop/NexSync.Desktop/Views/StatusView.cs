using System;
using System.Windows.Controls;

namespace NexSync.Desktop.Views;

public sealed class StatusView : System.Windows.Controls.UserControl
{
    private readonly TextBox _log;
    private readonly TextBlock _status;

    public StatusView(AppState state)
    {
        var panel = new System.Windows.Controls.StackPanel { Margin = new System.Windows.Thickness(24) };
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
        panel.Children.Add(_status);
        panel.Children.Add(syncNow);
        panel.Children.Add(new System.Windows.Controls.Label { Content = "Log" });
        panel.Children.Add(_log);
        panel.Children.Add(back);
        Content = panel;
    }

    public void AppendLog(string line)
    {
        _log.AppendText($"[{DateTime.Now:HH:mm:ss}] {line}{Environment.NewLine}");
    }
}


