using System.Drawing;
using System.Windows.Forms;

namespace PinchZoom;

/// <summary>
/// Tray icon and its context menu. The app has no window; this is its only UI.
/// </summary>
internal sealed class TrayHost : IDisposable
{
    private readonly NotifyIcon _icon;

    public event EventHandler? ExitRequested;

    public TrayHost()
    {
        var menu = new ContextMenuStrip();
        menu.Items.Add("Exit", null, (_, _) => ExitRequested?.Invoke(this, EventArgs.Empty));

        _icon = new NotifyIcon
        {
            Icon = SystemIcons.Application,
            Text = "PinchZoom",
            ContextMenuStrip = menu,
            Visible = true,
        };
    }

    public void Dispose()
    {
        // Hide before disposing so Windows removes the icon right away instead of leaving a ghost.
        _icon.Visible = false;
        _icon.ContextMenuStrip?.Dispose();
        _icon.Dispose();
    }
}
