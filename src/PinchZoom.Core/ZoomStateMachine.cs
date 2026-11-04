namespace PinchZoom.Core;

public enum ZoomState
{
    Idle,
    ZoomingIn,
    Zoomed,
    ZoomingOut,
}

/// <summary>Easing curves for the zoom animation.</summary>
public static class Easing
{
    /// <summary>Cubic ease-out: fast start, gentle stop. Input is clamped to [0, 1]; output is in [0, 1].</summary>
    public static double EaseOutCubic(double t)
    {
        t = Math.Clamp(t, 0.0, 1.0);
        var inv = 1.0 - t;
        return 1.0 - inv * inv * inv;
    }
}

/// <summary>
/// Hold-to-zoom state machine: Idle, ZoomingIn, Zoomed, ZoomingOut.
/// Pure and time-driven (the caller passes the current time), so it is deterministic in tests.
/// Not thread-safe; <see cref="ZoomController"/> serializes access.
/// </summary>
public sealed class ZoomStateMachine
{
    public const double MaxZoom = 5.0;
    public const double DefaultTargetZoom = 2.5;
    public static readonly TimeSpan DefaultHoldThreshold = TimeSpan.FromMilliseconds(150);
    public static readonly TimeSpan DefaultZoomInDuration = TimeSpan.FromMilliseconds(150);
    public static readonly TimeSpan DefaultZoomOutDuration = TimeSpan.FromMilliseconds(120);
    public static readonly TimeSpan DefaultWheelDuration = TimeSpan.FromMilliseconds(100);

    /// <summary>Zoom multiplier for one wheel notch (a delta of 120); smaller deltas scale it proportionally.</summary>
    public const double WheelStepFactor = 1.25;

    private TimeSpan? _pressedAt;
    private bool _held;
    private double _from = 1.0;
    private double _to = 1.0;
    private TimeSpan _segmentStart;
    private TimeSpan _segmentDuration;

    public ZoomStateMachine(
        double targetZoom = DefaultTargetZoom,
        TimeSpan? holdThreshold = null,
        TimeSpan? zoomInDuration = null,
        TimeSpan? zoomOutDuration = null,
        TimeSpan? wheelDuration = null)
    {
        if (double.IsNaN(targetZoom) || targetZoom <= 1.0 || targetZoom > MaxZoom)
            throw new ArgumentOutOfRangeException(nameof(targetZoom), targetZoom, $"Target zoom must be above 1 and at most {MaxZoom}.");

        TargetZoom = targetZoom;
        HoldThreshold = NonNegative(holdThreshold ?? DefaultHoldThreshold, nameof(holdThreshold));
        ZoomInDuration = NonNegative(zoomInDuration ?? DefaultZoomInDuration, nameof(zoomInDuration));
        ZoomOutDuration = NonNegative(zoomOutDuration ?? DefaultZoomOutDuration, nameof(zoomOutDuration));
        WheelDuration = NonNegative(wheelDuration ?? DefaultWheelDuration, nameof(wheelDuration));
    }

    /// <summary>The level a hold zooms to. The wheel can move away from it during a hold; each new hold starts here again.</summary>
    public double TargetZoom { get; }
    public TimeSpan HoldThreshold { get; }
    public TimeSpan ZoomInDuration { get; }
    public TimeSpan ZoomOutDuration { get; }
    public TimeSpan WheelDuration { get; }

    /// <summary>
    /// Idle; ZoomingIn (animating toward the held level, from the hold or the wheel); Zoomed (at the held level,
    /// which may be 1.0 if the wheel went all the way down); ZoomingOut (released).
    /// </summary>
    public ZoomState State { get; private set; } = ZoomState.Idle;

    /// <summary>Current zoom factor, as of the last <see cref="Advance"/> (or transition). Always within [1, MaxZoom].</summary>
    public double Zoom { get; private set; } = 1.0;

    /// <summary>The key is down but has not been held long enough to start zooming.</summary>
    public bool IsHoldPending => _pressedAt is not null;

    /// <summary>True while frames are needed: animating, zoomed, or waiting out the hold threshold.</summary>
    public bool IsActive => State != ZoomState.Idle || IsHoldPending;

    /// <summary>Key down. From Idle this arms the hold threshold; during zoom-out it reverses at once from the current factor.</summary>
    public void Press(TimeSpan now)
    {
        _held = true;
        switch (State)
        {
            case ZoomState.Idle:
                _pressedAt ??= now;
                break;
            case ZoomState.ZoomingOut:
                StartSegment(now, ZoomState.ZoomingIn);
                break;
        }
    }

    /// <summary>Key up. Cancels a pending hold (a tap does nothing); otherwise zooms out from the current factor.</summary>
    public void Release(TimeSpan now)
    {
        _held = false;
        _pressedAt = null;
        if (State is ZoomState.ZoomingIn or ZoomState.Zoomed)
            StartSegment(now, ZoomState.ZoomingOut);
    }

    /// <summary>
    /// Mouse wheel while the key is held: multiplies the level being headed for by <see cref="WheelStepFactor"/>
    /// per notch (positive = in), clamped to [1, MaxZoom], and animates there from the current factor.
    /// A wheel during the hold threshold starts zooming at once. Ignored when the key is not held.
    /// </summary>
    /// <param name="notches">Wheel delta divided by 120; fractional for high-resolution wheels.</param>
    public void Wheel(double notches, TimeSpan now)
    {
        if (!_held || double.IsNaN(notches) || notches == 0) return;

        _pressedAt = null;
        var heading = State is ZoomState.ZoomingIn ? _to : Zoom;
        var target = Math.Clamp(heading * Math.Pow(WheelStepFactor, notches), 1.0, MaxZoom);
        if (target == heading) return;

        // Bring the shown factor up to date first so the new segment starts exactly where the screen is.
        Advance(now);
        _from = Zoom;
        _to = target;
        _segmentStart = now;
        _segmentDuration = WheelDuration;
        State = ZoomState.ZoomingIn;
    }

    /// <summary>Moves the animation to <paramref name="now"/> and returns the zoom factor to show.</summary>
    public double Advance(TimeSpan now)
    {
        if (State == ZoomState.Idle && _pressedAt is { } pressedAt && now - pressedAt >= HoldThreshold)
        {
            _pressedAt = null;
            // Start from now rather than the threshold moment so the first frame does not jump ahead.
            StartSegment(now, ZoomState.ZoomingIn);
        }

        if (State is ZoomState.ZoomingIn or ZoomState.ZoomingOut)
        {
            var t = _segmentDuration <= TimeSpan.Zero
                ? 1.0
                : (now - _segmentStart).TotalMilliseconds / _segmentDuration.TotalMilliseconds;

            if (t >= 1.0)
            {
                Zoom = _to;
                // Wheeling all the way down while still holding stays Zoomed (at 1.0) so the wheel can go back up.
                State = _to <= 1.0 && !_held ? ZoomState.Idle : ZoomState.Zoomed;
            }
            else
            {
                Zoom = Math.Clamp(_from + (_to - _from) * Easing.EaseOutCubic(t), 1.0, MaxZoom);
            }
        }

        return Zoom;
    }

    private void StartSegment(TimeSpan now, ZoomState state)
    {
        // Start from the factor currently shown, so reversing mid-animation never jumps.
        _from = Zoom;
        _to = state == ZoomState.ZoomingIn ? TargetZoom : 1.0;
        _segmentStart = now;

        // A partial distance takes a proportional share of the full duration, keeping the speed consistent.
        // Capped at the full duration, so zooming out from a wheel level above TargetZoom is not slower.
        var full = state == ZoomState.ZoomingIn ? ZoomInDuration : ZoomOutDuration;
        var fraction = Math.Min(1.0, Math.Abs(_to - _from) / (TargetZoom - 1.0));
        _segmentDuration = TimeSpan.FromTicks((long)(full.Ticks * fraction));

        State = state;
    }

    private static TimeSpan NonNegative(TimeSpan value, string name) =>
        value >= TimeSpan.Zero ? value : throw new ArgumentOutOfRangeException(name, value, "Duration must not be negative.");
}
