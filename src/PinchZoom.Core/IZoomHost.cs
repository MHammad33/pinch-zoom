namespace PinchZoom.Core;

/// <summary>
/// Platform services <see cref="ZoomController"/> needs. Every member is called on the controller's
/// render thread, which is what the Magnification API's thread affinity requires.
/// Keeps Core free of Windows APIs; the app supplies the real implementation.
/// </summary>
public interface IZoomHost
{
    /// <summary>Starts the magnifier. Returns false (with an error code) instead of throwing.</summary>
    bool TryInitialize(out int errorCode);

    void SetTransform(float factor, int xOffset, int yOffset);

    /// <summary>Returns the screen to factor 1.0, offset 0. Must not throw.</summary>
    void Reset();

    /// <summary>Releases the magnifier. Must not throw.</summary>
    void Shutdown();

    (int X, int Y) GetCursorPosition();

    (int Width, int Height) GetScreenSize();

    /// <summary>True while the zoom key is physically down. Polled each frame to catch a missed key-up.</summary>
    bool IsZoomKeyDown();

    /// <summary>Blocks until the next display frame (vsync).</summary>
    void WaitForNextFrame();
}
