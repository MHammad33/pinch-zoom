using System.Windows;
using System.Windows.Threading;
using PinchZoom.Core;
using PinchZoom.Native;

namespace PinchZoom;

public partial class App : Application
{
    // Task 4: zoom is instant. Task 5 moves all engine calls into ZoomController and animates them.
    private const float HoldZoomFactor = 2.5f;

    private TrayHost? _tray;
    private MagnificationEngine? _engine;
    private HotkeyService? _hotkey;

    // A quick tap of Caps Lock does nothing; zoom starts only once the key is held this long.
    private readonly DispatcherTimer _holdTimer = new() { Interval = TimeSpan.FromMilliseconds(150) };

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // Never leave the screen magnified if something blows up.
        DispatcherUnhandledException += (_, _) => _engine?.Reset();
        AppDomain.CurrentDomain.UnhandledException += (_, _) => _engine?.Reset();

        _engine = new MagnificationEngine();
        _tray = new TrayHost();
        _tray.ExitRequested += (_, _) => Shutdown();

        if (!_engine.TryInitialize(out var magError))
        {
            _tray.ShowError($"Could not start magnifier (error {magError}).");
            return;
        }

        _holdTimer.Tick += (_, _) => { _holdTimer.Stop(); ZoomIn(); };

        _hotkey = new HotkeyService();
        _hotkey.Pressed += (_, _) => _holdTimer.Start();
        _hotkey.Released += (_, _) => { _holdTimer.Stop(); _engine.Reset(); };

        if (!_hotkey.TryInstall(out var hookError))
        {
            _tray.ShowError($"Could not install keyboard hook (error {hookError}).");
        }
    }

    private void ZoomIn()
    {
        if (_engine is not { IsInitialized: true }) return;

        // Anchor once at the cursor so the area under it stays put. Live tracking arrives in Task 6.
        var (w, h) = MagnificationEngine.PrimaryScreenSize;
        var (cx, cy) = CursorReader.GetPosition();
        var (xOff, yOff) = AnchorCalculator.Calculate(cx, cy, w, h, HoldZoomFactor);
        _engine.SetTransform(HoldZoomFactor, xOff, yOff);
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _holdTimer.Stop();
        _hotkey?.Dispose();
        _engine?.Dispose();
        _tray?.Dispose();
        base.OnExit(e);
    }
}
