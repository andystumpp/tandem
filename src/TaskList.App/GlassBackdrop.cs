using Microsoft.UI.Composition;
using Microsoft.UI.Composition.SystemBackdrops;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using Windows.UI;

namespace TaskList.App;

/// <summary>
/// Thin desktop acrylic with a very light tint, so the desktop shows through. Unlike the stock
/// backdrop it stays glassy when the window loses focus.
/// </summary>
public sealed class GlassBackdrop : SystemBackdrop
{
    private DesktopAcrylicController? _controller;
    private readonly SystemBackdropConfiguration _config = new() { IsInputActive = true };
    private Color _tint = Color.FromArgb(255, 255, 255, 255);
    private float _tintOpacity;
    private float _luminosity = 0.1f;

    protected override void OnTargetConnected(ICompositionSupportsSystemBackdrop connectedTarget, XamlRoot xamlRoot)
    {
        base.OnTargetConnected(connectedTarget, xamlRoot);
        _controller = new DesktopAcrylicController { Kind = DesktopAcrylicKind.Thin };
        _controller.AddSystemBackdropTarget(connectedTarget);
        _controller.SetSystemBackdropConfiguration(_config);
        // Set our tint last: applying a configuration resets the controller to theme defaults.
        ApplyToController();
    }

    protected override void OnTargetDisconnected(ICompositionSupportsSystemBackdrop disconnectedTarget)
    {
        base.OnTargetDisconnected(disconnectedTarget);
        _controller?.RemoveSystemBackdropTarget(disconnectedTarget);
        _controller?.Dispose();
        _controller = null;
    }

    /// <summary>
    /// The window's theme changes when the app switches between light and dark themes. The base
    /// implementation then rejects our target and crashes the app; we manage our own configuration
    /// (see <see cref="Update"/>), so there is nothing to do here.
    /// </summary>
    protected override void OnDefaultSystemBackdropConfigurationChanged(ICompositionSupportsSystemBackdrop target, XamlRoot xamlRoot)
    {
    }

    /// <param name="dark">Dark theme tints the glass dark instead of light.</param>
    /// <param name="frost">0 = clear glass, 1 = heavily frosted.</param>
    public void Update(bool dark, double frost)
    {
        frost = Math.Clamp(frost, 0, 1);
        _config.Theme = dark ? SystemBackdropTheme.Dark : SystemBackdropTheme.Light;
        _tint = dark ? Color.FromArgb(255, 20, 20, 26) : Color.FromArgb(255, 250, 250, 252);
        // The luminosity veil is what turns glass milky, so it stays low until the slider's right end;
        // the cards carry their own fill, so text stays readable even over clear glass.
        _tintOpacity = (float)(frost * 0.5);
        _luminosity = (float)(0.04 + frost * frost * 0.75);
        ApplyToController();
        // A theme change on the configuration resets the controller to the theme's default (gray)
        // glass a moment later, so apply our values again once that has happened.
        Microsoft.UI.Dispatching.DispatcherQueue.GetForCurrentThread()?.TryEnqueue(
            Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, ApplyToController);
    }

    private void ApplyToController()
    {
        if (_controller == null) return;
        _controller.TintColor = _tint;
        _controller.TintOpacity = _tintOpacity;
        _controller.LuminosityOpacity = _luminosity;
        _controller.FallbackColor = Color.FromArgb(230, _tint.R, _tint.G, _tint.B);
    }
}
