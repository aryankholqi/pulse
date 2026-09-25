using System;
using System.Globalization;
using System.Windows.Media;

namespace Pulse;

internal static class ColorUtil
{
    /// <summary>"#RGB", "#RRGGBB" or "#AARRGGBB" (the '#' is optional); anything else → <paramref name="fallback"/>.</summary>
    public static Color Parse(string? hex, string fallback) =>
        TryParse(hex, out var c) ? c : TryParse(fallback, out c) ? c : Colors.White;

    public static bool TryParse(string? hex, out Color color)
    {
        color = default;
        if (string.IsNullOrWhiteSpace(hex)) return false;
        string h = hex.Trim().TrimStart('#');
        if (h.Length == 3) h = string.Concat(h[0], h[0], h[1], h[1], h[2], h[2]);
        if (h.Length == 6) h = "FF" + h;
        if (h.Length != 8 || !uint.TryParse(h, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out uint v)) return false;
        color = Color.FromArgb((byte)(v >> 24), (byte)(v >> 16), (byte)(v >> 8), (byte)v);
        return true;
    }

    public static string ToHex(Color c) => $"#{c.R:X2}{c.G:X2}{c.B:X2}";

    public static Color Mix(Color a, Color b, double t)
    {
        t = Math.Clamp(t, 0, 1);
        return Color.FromArgb(a.A,
            (byte)Math.Round(a.R + (b.R - a.R) * t),
            (byte)Math.Round(a.G + (b.G - a.G) * t),
            (byte)Math.Round(a.B + (b.B - a.B) * t));
    }

    public static SolidColorBrush Solid(Color c) => Frozen(new SolidColorBrush(c));

    public static T Frozen<T>(T brush) where T : Brush
    {
        brush.Freeze();
        return brush;
    }
}
