using System.Drawing;
using System.Windows.Forms;

namespace PinchZoom;

/// <summary>
/// Tray icon and its context menu. The app has no window; this is its only UI.
/// </summary>
internal sealed class TrayHost : IDisposable
{
    private readonly NotifyIcon _icon;
    private readonly ToolStripMenuItem _testZoom = new("Test zoom") { CheckOnClick = true };

    public void SetTestZoomChecked(bool value) => _testZoom.Checked = value;

    public event EventHandler? ExitRequested;
    public event EventHandler<bool>? TestZoomToggled;

    public TrayHost()
    {
        var menu = new ContextMenuStrip();
        _testZoom.CheckedChanged += (_, _) => TestZoomToggled?.Invoke(this, _testZoom.Checked);
        menu.Items.Add(_testZoom);
        menu.Items.Add("Exit", null, (_, _) => ExitRequested?.Invoke(this, EventArgs.Empty));

        _icon = new NotifyIcon
        {
            Icon = SystemIcons.Application,
            Text = "PinchZoom",
            ContextMenuStrip = menu,
            Visible = true,
        };
    }

    public void ShowError(string message) =>
        _icon.ShowBalloonTip(3000, "PinchZoom", message, ToolTipIcon.Error);

    public void Dispose()
    {
        // Hide before disposing so Windows removes the icon right away instead of leaving a ghost.
        _icon.Visible = false;
        _icon.ContextMenuStrip?.Dispose();
        _icon.Dispose();
    }
}
