using System.Drawing.Imaging;
using System.Runtime.InteropServices;

namespace Orknux.Desktop;

/// <summary>
/// A picture of one window as the window draws itself, whatever covers it.
///
/// Copying the screen where a window sits shows whatever is on top there: when
/// Windows declined to bring Musigium forward, every "screenshot of Musigium"
/// was of the Chrome window in front of it, and the agent asked for it again
/// and again. <c>PrintWindow</c> with full content asks the window to paint
/// into a bitmap of its own, which works covered and for Chromium, WPF and
/// DirectComposition windows alike. A window that paints nothing that way - a
/// few still do - comes back as one flat colour, and is then copied off the
/// screen as before.
/// </summary>
internal static class WindowCapture
{
    private const uint PW_RENDERFULLCONTENT = 0x2;

    [DllImport("user32.dll")]
    private static extern bool PrintWindow(IntPtr hwnd, IntPtr hdc, uint flags);

    [DllImport("user32.dll")]
    private static extern bool GetWindowRect(IntPtr hwnd, out RECT rect);

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT { public int Left, Top, Right, Bottom; }

    /// <summary>The window painted into a bitmap, or null when it painted nothing that way.</summary>
    public static Bitmap? Print(IntPtr window)
    {
        if (!GetWindowRect(window, out var rect)) return null;
        var width = rect.Right - rect.Left;
        var height = rect.Bottom - rect.Top;
        if (width <= 0 || height <= 0) return null;
        var bitmap = new Bitmap(width, height, PixelFormat.Format32bppArgb);
        using (var graphics = Graphics.FromImage(bitmap))
        {
            var dc = graphics.GetHdc();
            try
            {
                if (!PrintWindow(window, dc, PW_RENDERFULLCONTENT))
                {
                    bitmap.Dispose();
                    return null;
                }
            }
            finally
            {
                graphics.ReleaseHdc(dc);
            }
        }
        if (Flat(bitmap))
        {
            bitmap.Dispose();
            return null;
        }
        return bitmap;
    }

    /// <summary>Whether every sampled pixel is one colour: what a window that would not paint gives back.</summary>
    private static bool Flat(Bitmap bitmap)
    {
        var first = bitmap.GetPixel(0, 0);
        for (var y = 0; y < bitmap.Height; y += Math.Max(1, bitmap.Height / 24))
        for (var x = 0; x < bitmap.Width; x += Math.Max(1, bitmap.Width / 24))
            if (bitmap.GetPixel(x, y) != first) return false;
        return true;
    }
}
