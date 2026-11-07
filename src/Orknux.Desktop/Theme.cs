using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace Orknux.Desktop;

/// <summary>The colours the window is drawn in, one set per Windows theme.</summary>
internal sealed record Palette(
    Color Background,
    Color Panel,
    Color Line,
    Color Ink,
    Color Muted,
    Color Accent,
    Color OnAccent,
    Color Good,
    Color Bad,
    bool Dark)
{
    public static readonly Palette DarkTheme = new(
        Background: Color.FromArgb(0x12, 0x12, 0x14),
        Panel: Color.FromArgb(0x1b, 0x1b, 0x1f),
        Line: Color.FromArgb(0x2c, 0x2c, 0x33),
        Ink: Color.FromArgb(0xe7, 0xe7, 0xea),
        Muted: Color.FromArgb(0x9a, 0x9a, 0xa3),
        Accent: Color.FromArgb(0x5b, 0x76, 0xd0),
        OnAccent: Color.White,
        Good: Color.FromArgb(0x4c, 0xc3, 0x8a),
        Bad: Color.FromArgb(0xe5, 0x48, 0x4d),
        Dark: true);

    // Green and red a shade deeper than on dark, or they wash out on white.
    public static readonly Palette LightTheme = new(
        Background: Color.FromArgb(0xf7, 0xf7, 0xf8),
        Panel: Color.White,
        Line: Color.FromArgb(0xd9, 0xd9, 0xde),
        Ink: Color.FromArgb(0x1b, 0x1b, 0x1f),
        Muted: Color.FromArgb(0x5f, 0x5f, 0x68),
        Accent: Color.FromArgb(0x4a, 0x66, 0xc4),
        OnAccent: Color.White,
        Good: Color.FromArgb(0x1a, 0x7f, 0x4b),
        Bad: Color.FromArgb(0xc4, 0x28, 0x2d),
        Dark: false);

    /// <summary>Each colour of this palette paired with the same role in <paramref name="next"/>.</summary>
    public IReadOnlyDictionary<Color, Color> To(Palette next)
    {
        var pairs = new Dictionary<Color, Color>();
        void Pair(Color from, Color to) => pairs.TryAdd(from, to);
        Pair(Background, next.Background);
        Pair(Panel, next.Panel);
        Pair(Line, next.Line);
        Pair(Ink, next.Ink);
        Pair(Muted, next.Muted);
        Pair(Accent, next.Accent);
        Pair(OnAccent, next.OnAccent);
        Pair(Good, next.Good);
        Pair(Bad, next.Bad);
        return pairs;
    }
}

/// <summary>
/// Follows the Windows theme - Settings › Personalisation › Colours, "Choose
/// your app mode" - for the window's colours and its title bar, which Windows
/// Forms leaves light unless asked: a dark window under a white bar is what it
/// looked like before.
/// </summary>
internal static class Theme
{
    private const string Personalize = @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize";

    /// <summary>The palette for the app mode Windows is in now; light where it cannot be read, as Windows itself defaults.</summary>
    public static Palette Current()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(Personalize);
            return key?.GetValue("AppsUseLightTheme") is int light && light == 0 ? Palette.DarkTheme : Palette.LightTheme;
        }
        catch (Exception)
        {
            return Palette.LightTheme;
        }
    }

    // 20 since Windows 10 20H1; 19 on the builds of 1809-1909 that had it under that number.
    private const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20;
    private const int DWMWA_USE_IMMERSIVE_DARK_MODE_OLD = 19;

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

    /// <summary>Draws the window's title bar dark or light. Harmless where Windows is too old to know how.</summary>
    public static void TitleBar(IntPtr window, bool dark)
    {
        var on = dark ? 1 : 0;
        if (DwmSetWindowAttribute(window, DWMWA_USE_IMMERSIVE_DARK_MODE, ref on, sizeof(int)) != 0)
            DwmSetWindowAttribute(window, DWMWA_USE_IMMERSIVE_DARK_MODE_OLD, ref on, sizeof(int));
    }

    /// <summary>Repaints <paramref name="control"/> and everything in it from one palette to the other, colour by colour.</summary>
    public static void Recolor(Control control, IReadOnlyDictionary<Color, Color> pairs)
    {
        if (pairs.TryGetValue(control.BackColor, out var back)) control.BackColor = back;
        if (pairs.TryGetValue(control.ForeColor, out var fore)) control.ForeColor = fore;
        if (control is Button button && pairs.TryGetValue(button.FlatAppearance.BorderColor, out var border))
            button.FlatAppearance.BorderColor = border;
        foreach (Control child in control.Controls) Recolor(child, pairs);
        control.Invalidate();
    }
}
