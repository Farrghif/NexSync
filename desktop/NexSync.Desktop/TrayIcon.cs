using System;
using System.Drawing;
using System.Windows.Forms;

namespace NexSync.Desktop;

public sealed class TrayIcon : IDisposable
{
    private readonly NotifyIcon _icon;

    public TrayIcon(Action onOpen, Action onSyncNow, Action onExit)
    {
        _icon = new NotifyIcon
        {
            Icon = SystemIcons.Application,
            Text = "NexSync",
            Visible = true,
            ContextMenuStrip = new ContextMenuStrip()
        };
        _icon.ContextMenuStrip.Items.Add("Open NexSync", null, (_, _) => onOpen());
        _icon.ContextMenuStrip.Items.Add("Sync Now", null, (_, _) => onSyncNow());
        _icon.ContextMenuStrip.Items.Add(new ToolStripSeparator());
        _icon.ContextMenuStrip.Items.Add("Exit", null, (_, _) => onExit());
        _icon.DoubleClick += (_, _) => onOpen();
    }

    public void Dispose()
    {
        _icon.Visible = false;
        _icon.Dispose();
    }
}
