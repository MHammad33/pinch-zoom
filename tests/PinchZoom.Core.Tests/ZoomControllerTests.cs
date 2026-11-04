using System.Collections.Concurrent;
using System.Diagnostics;
using System.Globalization;
using PinchZoom.Core;

namespace PinchZoom.Core.Tests;

public class ZoomControllerTests
{
    private sealed class FakeHost : IZoomHost
    {
        public bool InitSucceeds { get; init; } = true;
        public ConcurrentQueue<string> Calls { get; } = new();
        public ConcurrentBag<int> ThreadIds { get; } = new();
        public volatile float LastFactor = 1.0f;

        private void Record(string call)
        {
            ThreadIds.Add(Environment.CurrentManagedThreadId);
            Calls.Enqueue(call);
        }

        public bool TryInitialize(out int errorCode)
        {
            Record("init");
            errorCode = InitSucceeds ? 0 : 1400;
            return InitSucceeds;
        }

        public volatile bool ThrowOnSet;

        public void SetTransform(float factor, int xOffset, int yOffset)
        {
            if (ThrowOnSet) throw new InvalidOperationException("boom");
            Record(string.Create(CultureInfo.InvariantCulture, $"set {factor:0.###} {xOffset} {yOffset}"));
            LastFactor = factor;
        }

        public void Reset()
        {
            Record("reset");
            LastFactor = 1.0f;
        }

        public void Shutdown() => Record("shutdown");

        public volatile bool KeyDown = true;

        public bool IsZoomKeyDown() => KeyDown;

        public (int X, int Y) GetCursorPosition() => (960, 540);

        public (int Width, int Height) GetScreenSize() => (1920, 1080);

        public void WaitForNextFrame() => Thread.Sleep(1);
    }

    private static ZoomStateMachine FastMachine() =>
        new(2.0, TimeSpan.FromMilliseconds(10), TimeSpan.FromMilliseconds(30), TimeSpan.FromMilliseconds(30));

    private static void WaitUntil(Func<bool> condition)
    {
        var sw = Stopwatch.StartNew();
        while (!condition())
        {
            if (sw.Elapsed > TimeSpan.FromSeconds(5)) throw new TimeoutException("Condition not reached.");
            Thread.Sleep(2);
        }
    }

    [Fact]
    public void TryStart_ReportsInitFailure()
    {
        var host = new FakeHost { InitSucceeds = false };
        using var controller = new ZoomController(host, FastMachine());

        Assert.False(controller.TryStart(out var error));
        Assert.Equal(1400, error);
    }

    [Fact]
    public void HoldAndRelease_AnimatesToTargetAndBackToReset()
    {
        var host = new FakeHost();
        var controller = new ZoomController(host, FastMachine());
        Assert.True(controller.TryStart(out _));

        controller.Press();
        WaitUntil(() => controller.State == ZoomState.Zoomed);
        WaitUntil(() => host.LastFactor == 2.0f);

        // Final frame at 2x anchored on the screen centre: offset (480, 270).
        Assert.Contains("set 2 480 270", host.Calls);
        // Intermediate frames were shown on the way up.
        Assert.Contains(host.Calls, c => c.StartsWith("set 1."));

        controller.Release();
        WaitUntil(() => controller.State == ZoomState.Idle);
        WaitUntil(() => host.LastFactor == 1.0f);

        controller.Dispose();
        Assert.Equal("shutdown", host.Calls.Last());
    }

    [Fact]
    public void AllHostCalls_HappenOnOneRenderThread()
    {
        var host = new FakeHost();
        var controller = new ZoomController(host, FastMachine());
        Assert.True(controller.TryStart(out _));

        controller.Press();
        WaitUntil(() => controller.State == ZoomState.Zoomed);
        controller.Dispose();

        Assert.Single(host.ThreadIds.Distinct());
        Assert.NotEqual(Environment.CurrentManagedThreadId, host.ThreadIds.First());
    }

    [Fact]
    public void Dispose_WhileZoomed_ResetsScreen()
    {
        var host = new FakeHost();
        var controller = new ZoomController(host, FastMachine());
        Assert.True(controller.TryStart(out _));

        controller.Press();
        WaitUntil(() => controller.State == ZoomState.Zoomed);

        controller.Dispose();
        controller.Dispose(); // idempotent

        Assert.Equal(1.0f, host.LastFactor);
        Assert.Equal(new[] { "reset", "shutdown" }, host.Calls.TakeLast(2));
    }

    [Fact]
    public void MissedKeyUp_ReleasesZoomAfterGrace_AndRaisesEvent()
    {
        var host = new FakeHost();
        using var controller = new ZoomController(host, FastMachine());
        var raised = 0;
        controller.StuckKeyReleased += () => Interlocked.Increment(ref raised);
        Assert.True(controller.TryStart(out _));

        controller.Press();
        WaitUntil(() => controller.State == ZoomState.Zoomed);

        host.KeyDown = false; // the key-up never reaches Release()
        WaitUntil(() => controller.State == ZoomState.Idle);
        WaitUntil(() => host.LastFactor == 1.0f);
        Assert.Equal(1, raised);
    }

    [Fact]
    public void BriefKeyUpReading_DoesNotRelease()
    {
        var host = new FakeHost();
        using var controller = new ZoomController(host, FastMachine());
        Assert.True(controller.TryStart(out _));

        controller.Press();
        WaitUntil(() => controller.State == ZoomState.Zoomed);

        host.KeyDown = false;
        Thread.Sleep(20); // well under the grace period
        host.KeyDown = true;
        Thread.Sleep(ZoomController.StuckKeyGrace + TimeSpan.FromMilliseconds(100));

        Assert.Equal(ZoomState.Zoomed, controller.State);
    }

    [Fact]
    public void KeyReadingUpWhilePressIsPending_CancelsTheHold()
    {
        var host = new FakeHost { KeyDown = false };
        using var controller = new ZoomController(host, new ZoomStateMachine(2.0, TimeSpan.FromSeconds(10)));
        Assert.True(controller.TryStart(out _));

        controller.Press();
        Thread.Sleep(ZoomController.StuckKeyGrace + TimeSpan.FromMilliseconds(150));

        Assert.Equal(ZoomState.Idle, controller.State);
        Assert.DoesNotContain(host.Calls, c => c.StartsWith("set"));
    }

    [Fact]
    public void Tap_NeverTouchesTheScreen()
    {
        var host = new FakeHost();
        using var controller = new ZoomController(host, new ZoomStateMachine(2.0, TimeSpan.FromSeconds(10)));
        Assert.True(controller.TryStart(out _));

        controller.Press();
        Thread.Sleep(30);
        controller.Release();
        Thread.Sleep(30);

        Assert.DoesNotContain(host.Calls, c => c.StartsWith("set"));
        Assert.Equal(ZoomState.Idle, controller.State);
    }

    [Fact]
    public void Wheel_WhileHeld_ChangesZoom_AndReleaseResets()
    {
        var host = new FakeHost();
        using var controller = new ZoomController(host, FastMachine());
        Assert.True(controller.TryStart(out _));

        controller.Press();
        WaitUntil(() => controller.State == ZoomState.Zoomed && host.LastFactor == 2.0f);

        controller.Wheel(120);
        WaitUntil(() => host.LastFactor == 2.5f);

        controller.Release();
        WaitUntil(() => controller.State == ZoomState.Idle && host.LastFactor == 1.0f);
    }

    [Fact]
    public void FrameThatThrows_ResetsScreenAndRaisesFaulted()
    {
        var host = new FakeHost { ThrowOnSet = true };
        using var controller = new ZoomController(host, FastMachine());
        Exception? faulted = null;
        controller.Faulted += ex => faulted = ex;
        Assert.True(controller.TryStart(out _));

        controller.Press();
        WaitUntil(() => faulted is not null);

        Assert.IsType<InvalidOperationException>(faulted);
        Assert.Equal(["reset", "shutdown"], host.Calls.Skip(1).ToArray());
    }
}
