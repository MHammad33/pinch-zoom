using System.ComponentModel;
using System.Diagnostics;
using System.Windows;
using PinchZoom.Core;
using PinchZoom.Native;

namespace PinchZoom;

public partial class App : Application
{
    private TrayHost? _tray;
    private ZoomController? _zoom;
    private HotkeyService? _hotkey;
    private int _zoomKey = HotkeyService.VK_CAPITAL;
    private bool _paused;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // Never leave the screen magnified if something blows up: stopping the controller resets it.
        DispatcherUnhandledException += (_, args) => OnCrash(args.Exception);
        AppDomain.CurrentDomain.UnhandledException += (_, args) => OnCrash(args.ExceptionObject);

        _tray = new TrayHost();
        _tray.ExitRequested += (_, _) => Shutdown();
        _tray.PauseChanged += (_, paused) => SetPaused(paused);
        _tray.OpenConfigRequested += (_, _) => OpenConfig();
        _tray.ReloadConfigRequested += (_, _) => ReloadConfig();

        StartZoom();
    }

    private void OnCrash(object? error)
    {
        ErrorLog.Write("Unhandled exception", error);
        _zoom?.Dispose();
    }

    /// <summary>
    /// Loads the config, then starts the controller and (unless paused) the hook.
    /// Returns true when everything started with no problem to report.
    /// </summary>
    private bool StartZoom()
    {
        var loaded = ConfigStore.Load();
        if (loaded.Problems.Count > 0)
            _tray!.ShowError(string.Join(Environment.NewLine, loaded.Problems));

        _zoomKey = loaded.VirtualKey;

        var zoom = new ZoomController(new NativeZoomHost(_zoomKey), new ZoomStateMachine(loaded.Config.Zoom));
        zoom.Faulted += ex => Dispatcher.BeginInvoke(() =>
        {
            ErrorLog.Write("Render thread failed", ex);
            _tray?.ShowError("Zoom stopped after an error and the screen was reset. Use Reload config to restart it.");
        });

        // A hold released by the stuck-key poll never got its key-up, so let the hook start fresh.
        zoom.StuckKeyReleased += () => _hotkey?.ResyncUp();

        if (!zoom.TryStart(out var magError))
        {
            zoom.Dispose();
            ErrorLog.Write("MagInitialize failed", $"error {magError}");
            _tray!.ShowError($"Could not start magnifier (error {magError}).");
            return false;
        }

        _zoom = zoom;
        var hooked = _paused || InstallHook();
        return hooked && loaded.Problems.Count == 0;
    }

    private void StopZoom()
    {
        RemoveHook();
        _zoom?.Dispose();
        _zoom = null;
    }

    private bool InstallHook()
    {
        if (_zoom is null) return false;
        if (_hotkey is not null) return true;

        // Handlers run inside the keyboard hook, so they only record the event and return.
        var zoom = _zoom;
        var hotkey = new HotkeyService(_zoomKey);
        hotkey.Pressed += (_, _) => zoom.Press();
        hotkey.Released += (_, _) => zoom.Release();

        if (!hotkey.TryInstall(out var hookError))
        {
            ErrorLog.Write("Keyboard hook install failed", $"error {hookError}");
            _tray!.ShowError($"Could not install keyboard hook (error {hookError}).");
            return false;
        }

        _hotkey = hotkey;
        return true;
    }

    private void RemoveHook()
    {
        _hotkey?.Dispose();
        _hotkey = null;
    }

    private void SetPaused(bool paused)
    {
        _paused = paused;
        if (paused)
        {
            RemoveHook();
            _zoom?.Release();
        }
        else
        {
            InstallHook();
        }
    }

    private void ReloadConfig()
    {
        StopZoom();

        // Only confirm a clean reload; a problem already showed its own error balloon.
        if (StartZoom())
            _tray!.ShowInfo("Config reloaded.");
    }

    private void OpenConfig()
    {
        try
        {
            ConfigStore.EnsureExists();
            Process.Start(new ProcessStartInfo(ConfigStore.ConfigPath) { UseShellExecute = true });
        }
        catch (Win32Exception)
        {
            // No app is registered for .json files.
            TryOpenInNotepad();
        }
        catch (Exception ex) when (ex is System.IO.IOException or UnauthorizedAccessException)
        {
            _tray!.ShowError($"Could not create {ConfigStore.ConfigPath}: {ex.Message}");
        }
    }

    private void TryOpenInNotepad()
    {
        try
        {
            Process.Start("notepad.exe", $"\"{ConfigStore.ConfigPath}\"");
        }
        catch (Win32Exception ex)
        {
            _tray!.ShowError($"Could not open {ConfigStore.ConfigPath}: {ex.Message}");
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        StopZoom();
        _tray?.Dispose();
        base.OnExit(e);
    }
}
