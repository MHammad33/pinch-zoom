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

        public void SetTransform(float factor, int xOffset, int yOffset)
        {
            Record(string.Create(CultureInfo.InvariantCulture, $"set {factor:0.###} {xOffset} {yOffset}"));
            LastFactor = factor;
        }

        public void Reset()
        {
            Record("reset");
            LastFactor = 1.0f;
        }

        public void Shutdown() => Record("shutdown");

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
}
