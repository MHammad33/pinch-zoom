using System.Globalization;
using System.Windows;
using System.Windows.Input;
using PinchZoom.Core;

namespace PinchZoom;

/// <summary>
/// Edits the zoom key and zoom level. Save hands the new config to the app, which writes config.json and
/// applies it at once; the window stays open with a message if that fails.
/// </summary>
public partial class SettingsWindow : Window
{
    private readonly Func<PinchZoomConfig, string?> _save;
    private int _vk;
    private bool _capturing;

    /// <param name="save">Writes and applies the config; returns an error message, or null on success.</param>
    internal SettingsWindow(PinchZoomConfig current, int currentVirtualKey, Func<PinchZoomConfig, string?> save)
    {
        InitializeComponent();
        _save = save;
        _vk = currentVirtualKey;
        ZoomSlider.Value = Math.Clamp(current.Zoom, ZoomSlider.Minimum, ZoomSlider.Maximum);
        ShowKey();
        UpdateZoomText();
    }

    private void OnKeyButtonClick(object sender, RoutedEventArgs e)
    {
        _capturing = true;
        KeyButton.Content = "Press a key…  (Esc to cancel)";
        HideKeyMessage();
    }

    private void OnKeyButtonLostFocus(object sender, KeyboardFocusChangedEventArgs e) => StopCapture();

    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (!_capturing) return;

        // While capturing, every key belongs to the capture box (not Tab, Enter or Esc handling).
        e.Handled = true;

        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        if (key == Key.Escape)
        {
            StopCapture();
            return;
        }

        if (key is Key.None or Key.ImeProcessed or Key.DeadCharProcessed) return;

        var vk = KeyInterop.VirtualKeyFromKey(key);
        if (KeyNames.Check(vk) is { } problem)
        {
            ShowKeyMessage($"{KeyNames.ToDisplayName(vk)}: {problem}");
            return;
        }

        _vk = vk;
        HideKeyMessage();
        StopCapture();
    }

    private void StopCapture()
    {
        if (!_capturing) return;
        _capturing = false;
        ShowKey();
    }

    private void ShowKey() => KeyButton.Content = KeyNames.ToDisplayName(_vk);

    private void ShowKeyMessage(string text)
    {
        KeyMessage.Text = text;
        KeyMessage.Visibility = Visibility.Visible;
    }

    private void HideKeyMessage() => KeyMessage.Visibility = Visibility.Collapsed;

    private void OnZoomChanged(object sender, RoutedPropertyChangedEventArgs<double> e) => UpdateZoomText();

    private void UpdateZoomText()
    {
        // The slider fires while the window is still being built, before this label exists.
        if (ZoomValue is null) return;
        ZoomValue.Text = ZoomSlider.Value.ToString("0.0", CultureInfo.CurrentCulture) + "x";
    }

    private void OnSave(object sender, RoutedEventArgs e)
    {
        var config = new PinchZoomConfig(KeyNames.ToConfigName(_vk), Math.Round(ZoomSlider.Value, 1));
        if (_save(config) is { } error)
        {
            SaveMessage.Text = error;
            SaveMessage.Visibility = Visibility.Visible;
            return;
        }

        Close();
    }
}
