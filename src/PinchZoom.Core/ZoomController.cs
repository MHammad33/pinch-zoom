using System.Diagnostics;

namespace PinchZoom.Core;

/// <summary>
/// Drives <see cref="ZoomStateMachine"/> on a dedicated render thread paced by
/// <see cref="IZoomHost.WaitForNextFrame"/>, and is the only caller of the magnifier.
/// The render thread owns the magnifier for its whole life (initialize, transforms, reset, shutdown).
/// While idle it sleeps on an event; it only runs frames while a hold is pending or zoom is not 1.0.
/// <see cref="Press"/> and <see cref="Release"/> are cheap and safe to call from the keyboard hook.
/// </summary>
public sealed class ZoomController : IDisposable
{
    private readonly IZoomHost _host;
    private readonly ZoomStateMachine _machine;
    private readonly object _gate = new();
    private readonly AutoResetEvent _wake = new(false);
    private readonly Stopwatch _clock = Stopwatch.StartNew();

    private Thread? _thread;
    private volatile bool _stopping;
    private bool _initialized;
    private int _initError;

    private (int Width, int Height) _screen;

    public ZoomController(IZoomHost host, ZoomStateMachine machine)
    {
        _host = host ?? throw new ArgumentNullException(nameof(host));
        _machine = machine ?? throw new ArgumentNullException(nameof(machine));
    }

    public ZoomState State
    {
        get { lock (_gate) return _machine.State; }
    }

    /// <summary>
    /// Starts the render thread and waits for it to initialize the magnifier.
    /// Returns false (with the host's error code) if initialization fails; the thread then exits.
    /// </summary>
    public bool TryStart(out int errorCode)
    {
        if (_thread is not null) throw new InvalidOperationException("Already started.");

        using var ready = new ManualResetEventSlim(false);
        _thread = new Thread(() => Run(ready))
        {
            IsBackground = true,
            Name = "PinchZoom render",
            Priority = ThreadPriority.AboveNormal,
        };
        _thread.Start();
        ready.Wait();

        errorCode = _initError;
        return _initialized;
    }

    public void Press()
    {
        lock (_gate) _machine.Press(_clock.Elapsed);
        _wake.Set();
    }

    public void Release()
    {
        lock (_gate) _machine.Release(_clock.Elapsed);
        _wake.Set();
    }

    private void Run(ManualResetEventSlim ready)
    {
        if (!_host.TryInitialize(out var error))
        {
            _initError = error;
            ready.Set();
            return;
        }

        _initialized = true;
        ready.Set();

        try
        {
            var applied = (Zoom: 1.0, X: 0, Y: 0);
            while (!_stopping)
            {
                double zoom;
                bool active;
                lock (_gate)
                {
                    zoom = _machine.Advance(_clock.Elapsed);
                    active = _machine.IsActive;
                }

                if (zoom <= 1.0)
                {
                    if (applied.Zoom > 1.0) _host.Reset();
                    applied = (1.0, 0, 0);
                }
                else
                {
                    // The cursor is re-read every frame, so the view follows it while zoomed.
                    if (applied.Zoom <= 1.0) _screen = _host.GetScreenSize();
                    var (cx, cy) = _host.GetCursorPosition();
                    var (x, y) = AnchorCalculator.Calculate(cx, cy, _screen.Width, _screen.Height, zoom);
                    if (zoom != applied.Zoom || x != applied.X || y != applied.Y)
                    {
                        _host.SetTransform((float)zoom, x, y);
                        applied = (zoom, x, y);
                    }
                }

                if (active) _host.WaitForNextFrame();
                else _wake.WaitOne();
            }
        }
        catch
        {
            // An unhandled exception may skip finally blocks, so reset here before it propagates.
            _host.Reset();
            _host.Shutdown();
            throw;
        }

        _host.Reset();
        _host.Shutdown();
    }

    /// <summary>
    /// Stops the render thread, which resets the screen and shuts the magnifier down.
    /// Safe to call more than once and from crash handlers.
    /// </summary>
    public void Dispose()
    {
        _stopping = true;
        try { _wake.Set(); } catch (ObjectDisposedException) { return; }

        var thread = _thread;
        if (thread is null || thread == Thread.CurrentThread) return;

        if (thread.Join(TimeSpan.FromSeconds(1)))
            _wake.Dispose();
    }
}
