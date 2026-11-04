using System.Windows;
using PinchZoom.Core;
using PinchZoom.Native;

namespace PinchZoom;

public partial class App : Application
{
    // Task 2 only: a fixed zoom to prove the Magnification API works.
    // Task 5 moves all engine calls into ZoomController.
    private const float TestZoomFactor = 2.0f;

    private TrayHost? _tray;
    private MagnificationEngine? _engine;
    // Without live tracking the view cannot pan, so parts of the screen (often the taskbar)
    // stay out of reach. The test undoes itself as a safety net.
    private readonly System.Windows.Threading.DispatcherTimer _autoRevert = new() { Interval = TimeSpan.FromSeconds(5) };

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // Never leave the screen magnified if something blows up.
        DispatcherUnhandledException += (_, _) => _engine?.Reset();
        AppDomain.CurrentDomain.UnhandledException += (_, _) => _engine?.Reset();

        _engine = new MagnificationEngine();
        _tray = new TrayHost();
        _tray.ExitRequested += (_, _) => Shutdown();
        _tray.TestZoomToggled += OnTestZoomToggled;
        _autoRevert.Tick += (_, _) => { _autoRevert.Stop(); _tray.SetTestZoomChecked(false); };

        if (!_engine.TryInitialize(out var error))
        {
            _tray.ShowError($"Could not start magnifier (error {error}).");
        }
    }

    private void OnTestZoomToggled(object? sender, bool on)
    {
        if (_engine is not { IsInitialized: true }) return;

        _autoRevert.Stop();

        if (!on)
        {
            _engine.Reset();
            return;
        }

        // Anchor once at the cursor so the area under it stays put. Live tracking arrives in Task 6.
        var (w, h) = MagnificationEngine.PrimaryScreenSize;
        var (cx, cy) = CursorReader.GetPosition();
        var (xOff, yOff) = AnchorCalculator.Calculate(cx, cy, w, h, TestZoomFactor);
        _engine.SetTransform(TestZoomFactor, xOff, yOff);
        _autoRevert.Start();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _engine?.Dispose();
        _tray?.Dispose();
        base.OnExit(e);
    }
}
