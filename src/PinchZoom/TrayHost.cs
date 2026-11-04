using System.Drawing;
using System.Windows.Forms;

namespace PinchZoom;

/// <summary>
/// Tray icon and its context menu. The app has no window; this is its only UI.
/// </summary>
internal sealed class TrayHost : IDisposable
{
    private readonly NotifyIcon _icon;
    private readonly ToolStripMenuItem _pause;

    public event EventHandler? ExitRequested;
    public event EventHandler? SettingsRequested;

    /// <summary>Raised with the new state when the user ticks or unticks Pause zoom.</summary>
    public event EventHandler<bool>? PauseChanged;

    public TrayHost()
    {
        _pause = new ToolStripMenuItem("Pause zoom") { CheckOnClick = true };
        _pause.CheckedChanged += (_, _) => PauseChanged?.Invoke(this, _pause.Checked);

        var menu = new ContextMenuStrip();
        menu.Items.Add(_pause);
        menu.Items.Add("Settings…", null, (_, _) => SettingsRequested?.Invoke(this, EventArgs.Empty));
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Exit", null, (_, _) => ExitRequested?.Invoke(this, EventArgs.Empty));

        _icon = new NotifyIcon
        {
            Icon = SystemIcons.Application,
            Text = "PinchZoom",
            ContextMenuStrip = menu,
            Visible = true,
        };
        _icon.DoubleClick += (_, _) => SettingsRequested?.Invoke(this, EventArgs.Empty);
    }

    public void ShowError(string message) =>
        _icon.ShowBalloonTip(5000, "PinchZoom", message, ToolTipIcon.Error);

    public void ShowInfo(string message) =>
        _icon.ShowBalloonTip(3000, "PinchZoom", message, ToolTipIcon.Info);

    public void Dispose()
    {
        // Hide before disposing so Windows removes the icon right away instead of leaving a ghost.
        _icon.Visible = false;
        _icon.ContextMenuStrip?.Dispose();
        _icon.Dispose();
    }
}
