using System;
using System.Drawing;
using System.Drawing.Drawing2D;

namespace Pulse;

/// <summary>Draws the tray icon at runtime (ink tile + ivory pulse trace) — no .ico asset needed.</summary>
internal static class TrayIconFactory
{
    public static Icon Create()
    {
        using var bmp = new Bitmap(32, 32);
        using (var g = Graphics.FromImage(bmp))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(Color.Transparent);

            using var tile = RoundedRect(new RectangleF(1, 1, 30, 30), 8);
            using var glass = new LinearGradientBrush(new PointF(0, 0), new PointF(0, 32),
                Color.FromArgb(255, 0x2A, 0x31, 0x4E), Color.FromArgb(255, 0x13, 0x17, 0x27));
            g.FillPath(glass, tile);

            using var pen = new Pen(Color.FromArgb(255, 0xF6, 0xF1, 0xE7), 2.6f)
            {
                LineJoin = LineJoin.Round,
                StartCap = LineCap.Round,
                EndCap = LineCap.Round,
            };
            g.DrawLines(pen, new[]
            {
                new PointF(6, 17), new PointF(11, 17), new PointF(14, 9),
                new PointF(18, 24), new PointF(21, 15), new PointF(26, 15),
            });
        }

        IntPtr handle = bmp.GetHicon();
        try
        {
            using var temp = Icon.FromHandle(handle);
            return (Icon)temp.Clone();
        }
        finally
        {
            Native.DestroyIcon(handle);
        }
    }

    static GraphicsPath RoundedRect(RectangleF r, float radius)
    {
        float d = radius * 2;
        var p = new GraphicsPath();
        p.AddArc(r.X, r.Y, d, d, 180, 90);
        p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
        p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
        p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
        p.CloseFigure();
        return p;
    }
}
