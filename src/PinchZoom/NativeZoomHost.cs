using PinchZoom.Core;
using PinchZoom.Native;

namespace PinchZoom;

/// <summary>Connects <see cref="ZoomController"/> to the real magnifier, cursor and frame pacing.</summary>
internal sealed class NativeZoomHost : IZoomHost
{
    private readonly MagnificationEngine _engine = new();

    public bool TryInitialize(out int errorCode) => _engine.TryInitialize(out errorCode);

    public void SetTransform(float factor, int xOffset, int yOffset) => _engine.SetTransform(factor, xOffset, yOffset);

    public void Reset() => _engine.Reset();

    public void Shutdown() => _engine.Dispose();

    public (int X, int Y) GetCursorPosition() => CursorReader.GetPosition();

    public (int Width, int Height) GetScreenSize() => MagnificationEngine.PrimaryScreenSize;

    public void WaitForNextFrame() => FramePacer.WaitForNextFrame();
}
