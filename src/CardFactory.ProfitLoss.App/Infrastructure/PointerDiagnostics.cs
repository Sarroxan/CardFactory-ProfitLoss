using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;

namespace CardFactory.ProfitLoss.App.Infrastructure;

// Stage 6B.45: the mouse pointer sometimes vanishes over the main window - and only
// over it - while hover effects keep working, so Windows is still tracking it. The app
// never sets or hides a cursor, so there is no code to point at yet. This records what
// Windows and WPF each report each time the pointer enters the window, which tells
// "Windows says the pointer is hidden" apart from "a blank or null cursor is being
// drawn", and which element WPF thinks is under it.
internal static class PointerDiagnostics
{
    [StructLayout(LayoutKind.Sequential)]
    private struct Point { public int X; public int Y; }

    [StructLayout(LayoutKind.Sequential)]
    private struct CursorInfo { public int Size; public int Flags; public IntPtr Cursor; public Point Position; }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool GetCursorInfo(ref CursorInfo info);

    [DllImport("user32.dll")]
    private static extern IntPtr GetCursor();

    private const int CursorShowing = 0x1;
    private const int CursorSuppressed = 0x2;
    private static DateTime _last = DateTime.MinValue;

    public static void Log(Window window, string when)
    {
        try
        {
            if ((DateTime.Now - _last).TotalSeconds < 5) return;   // enough to see a pattern, not a flood
            _last = DateTime.Now;

            var info = new CursorInfo { Size = Marshal.SizeOf<CursorInfo>() };
            var state = GetCursorInfo(ref info)
                ? ((info.Flags & CursorShowing) != 0 ? "showing"
                   : (info.Flags & CursorSuppressed) != 0 ? "suppressed (Windows hid it for touch/pen)"
                   : "hidden")
                : "unknown (GetCursorInfo failed)";
            var over = Mouse.DirectlyOver as FrameworkElement;

            var text =
                "=== " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + " - pointer: " + when + " ===\r\n" +
                "  windows says   : " + state + ", flags " + info.Flags + ", cursor 0x" + info.Cursor.ToString("X") + "\r\n" +
                "  this thread    : cursor 0x" + GetCursor().ToString("X") + "\r\n" +
                "  wpf override   : " + (Mouse.OverrideCursor?.ToString() ?? "none") + "\r\n" +
                "  window cursor  : " + (window.Cursor?.ToString() ?? "none") + "\r\n" +
                "  under pointer  : " + (Mouse.DirectlyOver?.GetType().Name ?? "nothing") +
                    (string.IsNullOrEmpty(over?.Name) ? "" : " '" + over!.Name + "'") +
                    ", cursor " + (over?.Cursor?.ToString() ?? "none") + "\r\n";

            AppFiles.AppendDiagnostic(text);
        }
        catch
        {
            // Diagnostics must never affect the app.
        }
    }
}
