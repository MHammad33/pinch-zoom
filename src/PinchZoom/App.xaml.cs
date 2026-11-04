using System.IO;
using System.Windows;
using PinchZoom.Core;
using PinchZoom.Native;

namespace PinchZoom;

public partial class App : Application
{
    private TrayHost? _tray;
    private ZoomController? _zoom;
    private HotkeyService? _hotkey;
    private SettingsWindow? _settings;
    private PinchZoomConfig _config = PinchZoomConfig.Default;
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
        _tray.SettingsRequested += (_, _) => OpenSettings();

        StartZoom();
    }

    private void OnCrash(object? error)
    {
        ErrorLog.Write("Unhandled exception", error);
        _zoom?.Dispose();
    }

    /// <summary>Loads the config, then starts the controller and (unless paused) the hook.</summary>
    private void StartZoom()
    {
        var loaded = ConfigStore.Load();
        if (loaded.Problems.Count > 0)
            _tray!.ShowError(string.Join(Environment.NewLine, loaded.Problems));

        _config = loaded.Config;
        _zoomKey = loaded.VirtualKey;

        var zoom = new ZoomController(new NativeZoomHost(_zoomKey), new ZoomStateMachine(_config.Zoom));
        zoom.Faulted += ex => Dispatcher.BeginInvoke(() =>
        {
            ErrorLog.Write("Render thread failed", ex);
            _tray?.ShowError("Zoom stopped after an error and the screen was reset. Save settings or restart PinchZoom to turn it back on.");
        });

        // A hold released by the stuck-key poll never got its key-up, so let the hook start fresh.
        zoom.StuckKeyReleased += () => _hotkey?.ResyncUp();

        if (!zoom.TryStart(out var magError))
        {
            zoom.Dispose();
            ErrorLog.Write("MagInitialize failed", $"error {magError}");
            _tray!.ShowError($"Could not start magnifier (error {magError}).");
            return;
        }

        _zoom = zoom;
        if (!_paused) InstallHook();
    }

    private void StopZoom()
    {
        RemoveHook();
        _zoom?.Dispose();
        _zoom = null;
    }

    private void InstallHook()
    {
        if (_zoom is null || _hotkey is not null) return;

        // Handlers run inside the keyboard hook, so they only record the event and return.
        var zoom = _zoom;
        var hotkey = new HotkeyService(_zoomKey);
        hotkey.Pressed += (_, _) => zoom.Press();
        hotkey.Released += (_, _) => zoom.Release();

        if (!hotkey.TryInstall(out var hookError))
        {
            ErrorLog.Write("Keyboard hook install failed", $"error {hookError}");
            _tray!.ShowError($"Could not install keyboard hook (error {hookError}).");
            return;
        }

        _hotkey = hotkey;
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

    private void OpenSettings()
    {
        if (_settings is not null)
        {
            if (_settings.WindowState == WindowState.Minimized) _settings.WindowState = WindowState.Normal;
            _settings.Activate();
            return;
        }

        _settings = new SettingsWindow(_config, _zoomKey, ApplySettings);
        _settings.Closed += (_, _) => _settings = null;
        _settings.Show();
        _settings.Activate();
    }

    /// <summary>Writes the config and restarts zoom with it. Returns an error message, or null on success.</summary>
    private string? ApplySettings(PinchZoomConfig config)
    {
        try
        {
            ConfigStore.Save(config);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            ErrorLog.Write("Saving settings failed", ex);
            return $"Could not save settings: {ex.Message}";
        }

        StopZoom();
        StartZoom();
        return null;
    }

    protected override void OnExit(ExitEventArgs e)
    {
        StopZoom();
        _tray?.Dispose();
        base.OnExit(e);
    }
}
