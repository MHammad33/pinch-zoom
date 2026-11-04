using System.Windows;
using PinchZoom.Native;

namespace PinchZoom;

public partial class App : Application
{
    // Task 2 only: a fixed zoom to prove the Magnification API works.
    // Task 5 moves all engine calls into ZoomController.
    private const float TestZoomFactor = 2.0f;

    private TrayHost? _tray;
    private MagnificationEngine? _engine;
    // The fixed centre zoom hides the taskbar, so the test must undo itself.
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

        // Fixed centre anchor; cursor anchoring arrives in Task 3.
        var (w, h) = MagnificationEngine.PrimaryScreenSize;
        var xOff = (int)(w * (1 - 1 / TestZoomFactor) / 2);
        var yOff = (int)(h * (1 - 1 / TestZoomFactor) / 2);
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
