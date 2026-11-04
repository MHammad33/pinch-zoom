using System.Windows;
using PinchZoom.Core;
using PinchZoom.Native;

namespace PinchZoom;

public partial class App : Application
{
    private TrayHost? _tray;
    private ZoomController? _zoom;
    private HotkeyService? _hotkey;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // Never leave the screen magnified if something blows up: stopping the controller resets it.
        DispatcherUnhandledException += (_, _) => _zoom?.Dispose();
        AppDomain.CurrentDomain.UnhandledException += (_, _) => _zoom?.Dispose();

        _tray = new TrayHost();
        _tray.ExitRequested += (_, _) => Shutdown();

        _zoom = new ZoomController(new NativeZoomHost(), new ZoomStateMachine());
        if (!_zoom.TryStart(out var magError))
        {
            _tray.ShowError($"Could not start magnifier (error {magError}).");
            return;
        }

        // Handlers run inside the keyboard hook, so they only record the event and return.
        _hotkey = new HotkeyService();
        _hotkey.Pressed += (_, _) => _zoom.Press();
        _hotkey.Released += (_, _) => _zoom.Release();

        if (!_hotkey.TryInstall(out var hookError))
        {
            _tray.ShowError($"Could not install keyboard hook (error {hookError}).");
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _hotkey?.Dispose();
        _zoom?.Dispose();
        _tray?.Dispose();
        base.OnExit(e);
    }
}
