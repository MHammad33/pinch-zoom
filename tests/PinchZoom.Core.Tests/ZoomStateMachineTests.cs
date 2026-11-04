using PinchZoom.Core;

namespace PinchZoom.Core.Tests;

public class ZoomStateMachineTests
{
    private static TimeSpan Ms(double ms) => TimeSpan.FromMilliseconds(ms);

    private static ZoomStateMachine NewMachine() => new();

    /// <summary>Press at 0 and advance past the hold threshold so zoom-in starts at 150 ms.</summary>
    private static ZoomStateMachine StartedZoomIn()
    {
        var m = NewMachine();
        m.Press(Ms(0));
        m.Advance(Ms(150));
        return m;
    }

    private static ZoomStateMachine FullyZoomed()
    {
        var m = StartedZoomIn();
        m.Advance(Ms(150 + 150));
        return m;
    }

    [Fact]
    public void StartsIdleAtOne()
    {
        var m = NewMachine();
        Assert.Equal(ZoomState.Idle, m.State);
        Assert.Equal(1.0, m.Zoom);
        Assert.False(m.IsActive);
    }

    [Fact]
    public void Tap_ShorterThanThreshold_DoesNothing()
    {
        var m = NewMachine();
        m.Press(Ms(0));
        Assert.True(m.IsHoldPending);
        Assert.True(m.IsActive);

        m.Advance(Ms(100));
        m.Release(Ms(120));

        Assert.Equal(1.0, m.Advance(Ms(500)));
        Assert.Equal(ZoomState.Idle, m.State);
        Assert.False(m.IsActive);
    }

    [Fact]
    public void Hold_BeforeThreshold_StaysIdle()
    {
        var m = NewMachine();
        m.Press(Ms(0));

        Assert.Equal(1.0, m.Advance(Ms(149)));
        Assert.Equal(ZoomState.Idle, m.State);
    }

    [Fact]
    public void Hold_PastThreshold_ZoomsInWithoutJump()
    {
        var m = StartedZoomIn();

        Assert.Equal(ZoomState.ZoomingIn, m.State);
        Assert.Equal(1.0, m.Zoom);
        Assert.False(m.IsHoldPending);
    }

    [Fact]
    public void ZoomIn_ReachesTargetExactly_AfterDuration()
    {
        var m = StartedZoomIn();

        var mid = m.Advance(Ms(150 + 75));
        Assert.InRange(mid, 1.0, 2.5);
        Assert.NotEqual(1.0, mid);
        Assert.NotEqual(2.5, mid);
        Assert.Equal(ZoomState.ZoomingIn, m.State);

        Assert.Equal(2.5, m.Advance(Ms(150 + 150)));
        Assert.Equal(ZoomState.Zoomed, m.State);
        Assert.True(m.IsActive);
    }

    [Fact]
    public void ZoomIn_IsMonotonicAndSmooth()
    {
        var m = StartedZoomIn();
        var previous = 1.0;

        // 1 ms steps: no single frame may jump more than a small amount.
        for (var t = 151; t <= 300; t++)
        {
            var z = m.Advance(Ms(t));
            Assert.True(z >= previous, $"zoom went down at {t} ms");
            Assert.True(z - previous < 0.05, $"zoom jumped {z - previous} at {t} ms");
            previous = z;
        }

        Assert.Equal(2.5, previous);
    }

    [Fact]
    public void Release_WhenZoomed_ZoomsOutToExactlyOne()
    {
        var m = FullyZoomed();

        m.Release(Ms(1000));
        Assert.Equal(ZoomState.ZoomingOut, m.State);

        var mid = m.Advance(Ms(1060));
        Assert.InRange(mid, 1.0, 2.5);
        Assert.NotEqual(1.0, mid);

        Assert.Equal(1.0, m.Advance(Ms(1120)));
        Assert.Equal(ZoomState.Idle, m.State);
        Assert.False(m.IsActive);
    }

    [Fact]
    public void ZoomOut_IsMonotonic()
    {
        var m = FullyZoomed();
        m.Release(Ms(1000));
        var previous = 2.5;

        for (var t = 1001; t <= 1120; t++)
        {
            var z = m.Advance(Ms(t));
            Assert.True(z <= previous, $"zoom went up at {t} ms");
            previous = z;
        }

        Assert.Equal(1.0, previous);
    }

    [Fact]
    public void Release_DuringZoomIn_ReversesFromCurrentFactor()
    {
        var m = StartedZoomIn();
        var before = m.Advance(Ms(150 + 50));

        m.Release(Ms(200));
        Assert.Equal(ZoomState.ZoomingOut, m.State);

        // Same instant: no jump. Next frame: heading down.
        Assert.Equal(before, m.Advance(Ms(200)));
        Assert.True(m.Advance(Ms(210)) < before);
    }

    [Fact]
    public void Press_DuringZoomOut_ReversesAtOnceFromCurrentFactor()
    {
        var m = FullyZoomed();
        m.Release(Ms(1000));
        var before = m.Advance(Ms(1040));

        m.Press(Ms(1040));
        Assert.Equal(ZoomState.ZoomingIn, m.State);
        Assert.False(m.IsHoldPending);

        // No jump, no new hold threshold: it heads straight back up.
        Assert.Equal(before, m.Advance(Ms(1040)));
        Assert.True(m.Advance(Ms(1050)) > before);
    }

    [Fact]
    public void PartialReversal_TakesProportionalTime()
    {
        var m = StartedZoomIn();
        m.Advance(Ms(150 + 30));
        m.Release(Ms(180));

        // Less than the full 120 ms zoom-out is needed from a partial zoom.
        Assert.Equal(1.0, m.Advance(Ms(180 + 120)));
        Assert.Equal(ZoomState.Idle, m.State);
    }

    [Fact]
    public void RepeatedPress_WhileZoomed_IsIgnored()
    {
        var m = FullyZoomed();
        m.Press(Ms(500));

        Assert.Equal(ZoomState.Zoomed, m.State);
        Assert.Equal(2.5, m.Advance(Ms(600)));
    }

    [Fact]
    public void Release_WhenIdle_IsHarmless()
    {
        var m = NewMachine();
        m.Release(Ms(10));

        Assert.Equal(ZoomState.Idle, m.State);
        Assert.Equal(1.0, m.Advance(Ms(20)));
    }

    [Fact]
    public void ZoomAlwaysStaysWithinRange_UnderRandomInput()
    {
        var random = new Random(1234);
        var m = NewMachine();
        var down = false;

        for (var t = 0; t < 20_000; t += random.Next(1, 20))
        {
            if (random.NextDouble() < 0.05)
            {
                if (down) m.Release(Ms(t)); else m.Press(Ms(t));
                down = !down;
            }

            var z = m.Advance(Ms(t));
            Assert.InRange(z, 1.0, m.TargetZoom);
        }
    }

    [Fact]
    public void Wheel_WhenNotHeld_IsIgnored()
    {
        var m = NewMachine();
        m.Wheel(3, Ms(0));
        Assert.Equal(ZoomState.Idle, m.State);
        Assert.Equal(1.0, m.Advance(Ms(500)));

        var z = FullyZoomed();
        z.Release(Ms(1000));
        z.Wheel(3, Ms(1010));
        Assert.Equal(ZoomState.ZoomingOut, z.State);
    }

    [Fact]
    public void Wheel_WhenZoomed_AnimatesByStepFactor()
    {
        var m = FullyZoomed();
        m.Wheel(1, Ms(400));
        Assert.Equal(ZoomState.ZoomingIn, m.State);

        var mid = m.Advance(Ms(450));
        Assert.True(mid > 2.5 && mid < 2.5 * ZoomStateMachine.WheelStepFactor);

        Assert.Equal(2.5 * ZoomStateMachine.WheelStepFactor, m.Advance(Ms(500)), 9);
        Assert.Equal(ZoomState.Zoomed, m.State);
    }

    [Fact]
    public void Wheel_DuringHoldThreshold_StartsZoomingAtOnce()
    {
        var m = NewMachine();
        m.Press(Ms(0));
        m.Wheel(1, Ms(20));

        Assert.False(m.IsHoldPending);
        Assert.Equal(ZoomState.ZoomingIn, m.State);
        Assert.Equal(ZoomStateMachine.WheelStepFactor, m.Advance(Ms(200)), 9);
    }

    [Fact]
    public void Wheel_DuringZoomIn_StacksOnTheLevelBeingHeadedFor()
    {
        var m = StartedZoomIn();
        var before = m.Advance(Ms(170));

        m.Wheel(1, Ms(170));
        Assert.Equal(before, m.Advance(Ms(170))); // no jump
        Assert.Equal(2.5 * ZoomStateMachine.WheelStepFactor, m.Advance(Ms(400)), 9);
    }

    [Fact]
    public void Wheel_ClampsToMaxZoom()
    {
        var m = FullyZoomed();
        m.Wheel(50, Ms(400));
        Assert.Equal(ZoomStateMachine.MaxZoom, m.Advance(Ms(600)));

        m.Wheel(1, Ms(600)); // already at max: nothing to do
        Assert.Equal(ZoomState.Zoomed, m.State);
    }

    [Fact]
    public void Wheel_DownToOne_WhileHeld_StaysZoomedAndCanGoBackUp()
    {
        var m = FullyZoomed();
        m.Wheel(-50, Ms(400));
        Assert.Equal(1.0, m.Advance(Ms(600)));
        Assert.Equal(ZoomState.Zoomed, m.State);
        Assert.True(m.IsActive);

        m.Wheel(1, Ms(700));
        Assert.Equal(ZoomStateMachine.WheelStepFactor, m.Advance(Ms(900)), 9);
    }

    [Fact]
    public void Release_AfterWheelDownToOne_GoesIdle()
    {
        var m = FullyZoomed();
        m.Wheel(-50, Ms(400));
        m.Advance(Ms(600));

        m.Release(Ms(700));
        Assert.Equal(1.0, m.Advance(Ms(700)));
        Assert.Equal(ZoomState.Idle, m.State);
        Assert.False(m.IsActive);
    }

    [Fact]
    public void Release_FromMaxZoom_TakesNoLongerThanFullZoomOut()
    {
        var m = FullyZoomed();
        m.Wheel(50, Ms(400));
        m.Advance(Ms(600));

        m.Release(Ms(1000));
        Assert.Equal(1.0, m.Advance(Ms(1000 + 120)));
        Assert.Equal(ZoomState.Idle, m.State);
    }

    [Fact]
    public void NextHold_AfterWheel_StartsAtTargetZoomAgain()
    {
        var m = FullyZoomed();
        m.Wheel(3, Ms(400));
        m.Advance(Ms(600));
        m.Release(Ms(700));
        m.Advance(Ms(1000));

        m.Press(Ms(2000));
        m.Advance(Ms(2150));
        Assert.Equal(2.5, m.Advance(Ms(2300)));
    }

    [Fact]
    public void ZoomStaysWithinRange_UnderRandomWheelInput()
    {
        var random = new Random(4321);
        var m = NewMachine();
        var down = false;

        for (var t = 0; t < 20_000; t += random.Next(1, 20))
        {
            var roll = random.NextDouble();
            if (roll < 0.03)
            {
                if (down) m.Release(Ms(t)); else m.Press(Ms(t));
                down = !down;
            }
            else if (roll < 0.2)
            {
                m.Wheel(random.Next(-3, 4) * random.NextDouble(), Ms(t));
            }

            var z = m.Advance(Ms(t));
            Assert.InRange(z, 1.0, ZoomStateMachine.MaxZoom);
        }
    }

    [Theory]
    [InlineData(1.0)]
    [InlineData(0.5)]
    [InlineData(5.01)]
    [InlineData(double.NaN)]
    public void TargetZoomOutOfRange_Throws(double target)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new ZoomStateMachine(target));
    }

    [Fact]
    public void ZeroDurations_SnapImmediately()
    {
        var m = new ZoomStateMachine(2.0, TimeSpan.Zero, TimeSpan.Zero, TimeSpan.Zero);
        m.Press(Ms(0));
        Assert.Equal(2.0, m.Advance(Ms(0)));
        Assert.Equal(ZoomState.Zoomed, m.State);

        m.Release(Ms(1));
        Assert.Equal(1.0, m.Advance(Ms(1)));
        Assert.Equal(ZoomState.Idle, m.State);
    }
}

public class EasingTests
{
    [Fact]
    public void EaseOutCubic_HitsEndpoints()
    {
        Assert.Equal(0.0, Easing.EaseOutCubic(0.0));
        Assert.Equal(1.0, Easing.EaseOutCubic(1.0));
    }

    [Theory]
    [InlineData(-1.0, 0.0)]
    [InlineData(2.0, 1.0)]
    public void EaseOutCubic_ClampsInput(double t, double expected)
    {
        Assert.Equal(expected, Easing.EaseOutCubic(t));
    }

    [Fact]
    public void EaseOutCubic_IsMonotonicAndBounded()
    {
        var previous = 0.0;
        for (var i = 1; i <= 1000; i++)
        {
            var v = Easing.EaseOutCubic(i / 1000.0);
            Assert.InRange(v, 0.0, 1.0);
            Assert.True(v >= previous);
            previous = v;
        }
    }

    [Fact]
    public void EaseOutCubic_IsAheadOfLinear()
    {
        // Ease-out: most of the movement happens early.
        Assert.True(Easing.EaseOutCubic(0.5) > 0.5);
    }
}
