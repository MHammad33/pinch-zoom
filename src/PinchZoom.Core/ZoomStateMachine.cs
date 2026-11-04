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

    private TimeSpan? _pressedAt;
    private double _from = 1.0;
    private double _to = 1.0;
    private TimeSpan _segmentStart;
    private TimeSpan _segmentDuration;

    public ZoomStateMachine(
        double targetZoom = DefaultTargetZoom,
        TimeSpan? holdThreshold = null,
        TimeSpan? zoomInDuration = null,
        TimeSpan? zoomOutDuration = null)
    {
        if (double.IsNaN(targetZoom) || targetZoom <= 1.0 || targetZoom > MaxZoom)
            throw new ArgumentOutOfRangeException(nameof(targetZoom), targetZoom, $"Target zoom must be above 1 and at most {MaxZoom}.");

        TargetZoom = targetZoom;
        HoldThreshold = NonNegative(holdThreshold ?? DefaultHoldThreshold, nameof(holdThreshold));
        ZoomInDuration = NonNegative(zoomInDuration ?? DefaultZoomInDuration, nameof(zoomInDuration));
        ZoomOutDuration = NonNegative(zoomOutDuration ?? DefaultZoomOutDuration, nameof(zoomOutDuration));
    }

    public double TargetZoom { get; }
    public TimeSpan HoldThreshold { get; }
    public TimeSpan ZoomInDuration { get; }
    public TimeSpan ZoomOutDuration { get; }

    public ZoomState State { get; private set; } = ZoomState.Idle;

    /// <summary>Current zoom factor, as of the last <see cref="Advance"/> (or transition). Always within [1, TargetZoom].</summary>
    public double Zoom { get; private set; } = 1.0;

    /// <summary>The key is down but has not been held long enough to start zooming.</summary>
    public bool IsHoldPending => _pressedAt is not null;

    /// <summary>True while frames are needed: animating, zoomed, or waiting out the hold threshold.</summary>
    public bool IsActive => State != ZoomState.Idle || IsHoldPending;

    /// <summary>Key down. From Idle this arms the hold threshold; during zoom-out it reverses at once from the current factor.</summary>
    public void Press(TimeSpan now)
    {
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
        _pressedAt = null;
        if (State is ZoomState.ZoomingIn or ZoomState.Zoomed)
            StartSegment(now, ZoomState.ZoomingOut);
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
                State = _to <= 1.0 ? ZoomState.Idle : ZoomState.Zoomed;
            }
            else
            {
                Zoom = Math.Clamp(_from + (_to - _from) * Easing.EaseOutCubic(t), 1.0, TargetZoom);
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
        var full = state == ZoomState.ZoomingIn ? ZoomInDuration : ZoomOutDuration;
        var fraction = Math.Abs(_to - _from) / (TargetZoom - 1.0);
        _segmentDuration = TimeSpan.FromTicks((long)(full.Ticks * fraction));

        State = state;
    }

    private static TimeSpan NonNegative(TimeSpan value, string name) =>
        value >= TimeSpan.Zero ? value : throw new ArgumentOutOfRangeException(name, value, "Duration must not be negative.");
}
