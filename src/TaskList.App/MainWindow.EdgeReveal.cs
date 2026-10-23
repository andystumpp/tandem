using System.Runtime.InteropServices;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Windowing;

namespace TaskList.App;

/// <summary>
/// Hot edge: resting the mouse against the right edge of the screen for a moment brings the app to the
/// front (restoring it if minimized). Ignored while a mouse button is held, so dragging past the edge
/// doesn't trigger it.
/// </summary>
public sealed partial class MainWindow
{
    private static readonly TimeSpan EdgeDwell = TimeSpan.FromMilliseconds(300);
    private DispatcherQueueTimer? _edgeTimer;
    private DateTime? _edgeSince;

    private void InitEdgeReveal()
    {
        _edgeTimer = DispatcherQueue.CreateTimer();
        _edgeTimer.Interval = TimeSpan.FromMilliseconds(120);
        _edgeTimer.Tick += (_, _) => CheckEdge();
        if (_prefs.RevealAtRightEdge) _edgeTimer.Start();
    }

    private void SetEdgeReveal(bool on)
    {
        _prefs.RevealAtRightEdge = on;
        _prefs.Save();
        if (on) _edgeTimer?.Start();
        else _edgeTimer?.Stop();
    }

    private void CheckEdge()
    {
        if (!GetCursorPos(out var cursor) || (GetAsyncKeyState(0x01) & 0x8000) != 0 || (GetAsyncKeyState(0x02) & 0x8000) != 0)
        {
            _edgeSince = null;
            return;
        }
        var monitor = DisplayArea.GetFromPoint(new Windows.Graphics.PointInt32(cursor.X, cursor.Y), DisplayAreaFallback.Nearest).OuterBounds;
        bool atEdge = cursor.X >= monitor.X + monitor.Width - 2;
        if (!atEdge)
        {
            _edgeSince = null;
            return;
        }
        _edgeSince ??= DateTime.Now;
        if (DateTime.Now - _edgeSince < EdgeDwell) return;
        _edgeSince = DateTime.MaxValue; // once per visit to the edge

        var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
        if (GetForegroundWindow() == hwnd) return;
        if (AppWindow.Presenter is OverlappedPresenter { State: OverlappedPresenterState.Minimized } presenter) presenter.Restore();
        // Windows only lets the foreground app hand over focus; a tap of Alt counts as user input.
        keybd_event(0x12, 0, 0, UIntPtr.Zero);
        SetForegroundWindow(hwnd);
        keybd_event(0x12, 0, 2, UIntPtr.Zero);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct CursorPoint { public int X, Y; }

    [DllImport("user32.dll")] private static extern bool GetCursorPos(out CursorPoint point);
    [DllImport("user32.dll")] private static extern short GetAsyncKeyState(int key);
    [DllImport("user32.dll")] private static extern bool SetForegroundWindow(IntPtr hwnd);
    [DllImport("user32.dll")] private static extern void keybd_event(byte key, byte scan, uint flags, UIntPtr extra);
}
