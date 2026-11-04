using System.Diagnostics;

namespace PinchZoom.Core;

/// <summary>
/// Drives <see cref="ZoomStateMachine"/> on a dedicated render thread paced by
/// <see cref="IZoomHost.WaitForNextFrame"/>, and is the only caller of the magnifier.
/// The render thread owns the magnifier for its whole life (initialize, transforms, reset, shutdown).
/// While idle it sleeps on an event; it only runs frames while a hold is pending or zoom is not 1.0.
/// <see cref="Press"/>, <see cref="Release"/> and <see cref="Wheel"/> are cheap and safe to call from the input hooks.
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

    // Captured when a zoom starts from 1.0, so the area under the cursor stays put. The view stays fixed while zoomed.
    private (int X, int Y) _anchor;
    private (int Width, int Height) _screen;
    private TimeSpan? _upSince;

    /// <summary>How long the key must read as up before a missed key-up is assumed.</summary>
    public static readonly TimeSpan StuckKeyGrace = TimeSpan.FromMilliseconds(100);

    /// <summary>
    /// Raised on the render thread when the controller released a hold because the key read as up
    /// (its key-up never arrived). Handlers must return quickly and not call back into the controller.
    /// </summary>
    public event Action? StuckKeyReleased;

    /// <summary>
    /// Raised on the render thread when a frame threw. The screen has been reset and the render thread has
    /// stopped, so the controller no longer zooms; create a new one to recover. Handlers must not throw.
    /// </summary>
    public event Action<Exception>? Faulted;

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
        lock (_gate)
        {
            _upSince = null;
            _machine.Press(_clock.Elapsed);
        }

        _wake.Set();
    }

    public void Release()
    {
        lock (_gate) _machine.Release(_clock.Elapsed);
        _wake.Set();
    }

    /// <summary>One wheel delta (120 per notch, positive = away from the user = zoom in). Ignored unless the key is held.</summary>
    public void Wheel(int delta)
    {
        lock (_gate) _machine.Wheel(delta / 120.0, _clock.Elapsed);
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
            var applied = 1.0;
            while (!_stopping)
            {
                double zoom;
                bool active;
                bool stuck;
                lock (_gate)
                {
                    stuck = PollStuckKey();
                    zoom = _machine.Advance(_clock.Elapsed);
                    active = _machine.IsActive;
                }

                if (stuck) StuckKeyReleased?.Invoke();

                if (zoom != applied)
                {
                    Apply(zoom, startingFromIdle: applied <= 1.0);
                    applied = zoom;
                }

                if (active) _host.WaitForNextFrame();
                else _wake.WaitOne();
            }
        }
        catch (Exception ex)
        {
            // Never leave the screen magnified: reset, stop this thread, and let the app report it.
            try
            {
                _host.Reset();
                _host.Shutdown();
            }
            catch
            {
                // The process ending also resets the transform; nothing more to do here.
            }

            Faulted?.Invoke(ex);
            return;
        }

        _host.Reset();
        _host.Shutdown();
    }

    /// <summary>
    /// Releases the machine if the key is no longer down (a key-up the hook missed). The key must read as up for
    /// <see cref="StuckKeyGrace"/> first, so the short gap between the hook's key-down and the host's key state
    /// catching up is never mistaken for a release. Returns true when it released. Call with <c>_gate</c> held.
    /// </summary>
    private bool PollStuckKey()
    {
        if (!_machine.IsActive || _host.IsZoomKeyDown())
        {
            _upSince = null;
            return false;
        }

        var now = _clock.Elapsed;
        _upSince ??= now;
        if (now - _upSince < StuckKeyGrace) return false;

        _upSince = null;
        _machine.Release(now);
        return true;
    }

    private void Apply(double zoom, bool startingFromIdle)
    {
        if (zoom <= 1.0)
        {
            _host.Reset();
            return;
        }

        if (startingFromIdle)
        {
            _anchor = _host.GetCursorPosition();
            _screen = _host.GetScreenSize();
        }

        var (x, y) = AnchorCalculator.Calculate(_anchor.X, _anchor.Y, _screen.Width, _screen.Height, zoom);
        _host.SetTransform((float)zoom, x, y);
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
