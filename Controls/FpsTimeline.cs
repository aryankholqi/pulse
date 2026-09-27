using System;
using System.Globalization;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;

namespace Pulse.Controls;

/// <summary>
/// FPS across a whole session: one line over a quiet grid, the session average as a dashed
/// reference, and a crosshair + readout under the mouse. Drawn in one OnRender pass.
/// </summary>
public sealed class FpsTimeline : FrameworkElement
{
    static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    const double Left = 30, Bottom = 18, Top = 8, Right = 4;

    public static readonly DependencyProperty ValuesProperty = Register(nameof(Values), typeof(float[]), null);
    public static readonly DependencyProperty SecondsPerPointProperty = Register(nameof(SecondsPerPoint), typeof(double), 5d);
    public static readonly DependencyProperty AverageProperty = Register(nameof(Average), typeof(double), 0d);
    public static readonly DependencyProperty StrokeProperty = Register(nameof(Stroke), typeof(Brush), Brushes.White);
    public static readonly DependencyProperty GridProperty = Register(nameof(Grid), typeof(Brush), Brushes.Gray);
    public static readonly DependencyProperty LabelProperty = Register(nameof(Label), typeof(Brush), Brushes.Gray);
    public static readonly DependencyProperty SurfaceProperty = Register(nameof(Surface), typeof(Brush), Brushes.Black);
    public static readonly DependencyProperty TipProperty = Register(nameof(Tip), typeof(Brush), Brushes.Black);
    public static readonly DependencyProperty TipTextProperty = Register(nameof(TipText), typeof(Brush), Brushes.White);

    static DependencyProperty Register(string name, Type type, object? fallback) =>
        DependencyProperty.Register(name, type, typeof(FpsTimeline),
            new FrameworkPropertyMetadata(fallback, FrameworkPropertyMetadataOptions.AffectsRender));

    public float[]? Values { get => (float[]?)GetValue(ValuesProperty); set => SetValue(ValuesProperty, value); }
    public double SecondsPerPoint { get => (double)GetValue(SecondsPerPointProperty); set => SetValue(SecondsPerPointProperty, value); }
    public double Average { get => (double)GetValue(AverageProperty); set => SetValue(AverageProperty, value); }
    public Brush Stroke { get => (Brush)GetValue(StrokeProperty); set => SetValue(StrokeProperty, value); }
    public Brush Grid { get => (Brush)GetValue(GridProperty); set => SetValue(GridProperty, value); }
    public Brush Label { get => (Brush)GetValue(LabelProperty); set => SetValue(LabelProperty, value); }
    /// <summary>The card behind the chart: rings the hover dot so it reads on top of the line.</summary>
    public Brush Surface { get => (Brush)GetValue(SurfaceProperty); set => SetValue(SurfaceProperty, value); }
    public Brush Tip { get => (Brush)GetValue(TipProperty); set => SetValue(TipProperty, value); }
    public Brush TipText { get => (Brush)GetValue(TipTextProperty); set => SetValue(TipTextProperty, value); }

    int _hover = -1;

    public FpsTimeline()
    {
        FlowDirection = FlowDirection.LeftToRight; // time runs left to right in every language
        Cursor = Cursors.Cross;
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        var v = Values;
        if (v is null || v.Length == 0) return;
        double x = e.GetPosition(this).X;
        double plotW = ActualWidth - Left - Right;
        int i = v.Length == 1 ? 0 : (int)Math.Round((x - Left) / plotW * (v.Length - 1));
        i = Math.Clamp(i, 0, v.Length - 1);
        if (i != _hover) { _hover = i; InvalidateVisual(); }
    }

    protected override void OnMouseLeave(MouseEventArgs e)
    {
        _hover = -1;
        InvalidateVisual();
    }

    protected override void OnRender(DrawingContext dc)
    {
        double w = ActualWidth, h = ActualHeight;
        if (w <= Left + Right || h <= Top + Bottom) return;

        // hit target: the whole plot, not just the line
        dc.DrawRectangle(Brushes.Transparent, null, new Rect(0, 0, w, h));

        var v = Values;
        if (v is null || v.Length == 0) return;

        double pixelsPerDip = VisualTreeHelper.GetDpi(this).PixelsPerDip;
        double plotW = w - Left - Right, plotH = h - Top - Bottom;
        double bottomY = Top + plotH;

        double max = 0;
        foreach (float f in v) max = Math.Max(max, f);
        double top = NiceCeiling(Math.Max(max * 1.08, 10));

        double Y(double fps) => bottomY - Math.Clamp(fps / top, 0, 1) * plotH;
        double X(int i) => v.Length == 1 ? Left + plotW / 2 : Left + i * plotW / (v.Length - 1);

        // recessive grid: 0, half, top — labelled on the left
        var gridPen = new Pen(Grid, 1);
        gridPen.Freeze();
        foreach (double g in new[] { 0, top / 2, top })
        {
            double y = Math.Round(Y(g)) + 0.5;
            dc.DrawLine(gridPen, new Point(Left, y), new Point(w - Right, y));
            var text = Text(g.ToString("0", Inv), 10.5, Label, pixelsPerDip);
            dc.DrawText(text, new Point(Left - 6 - text.Width, y - text.Height / 2));
        }

        // time axis: start and end only
        var start = Text("0:00", 10.5, Label, pixelsPerDip);
        dc.DrawText(start, new Point(Left, bottomY + 4));
        var end = Text(Clock(v.Length * SecondsPerPoint), 10.5, Label, pixelsPerDip);
        dc.DrawText(end, new Point(w - Right - end.Width, bottomY + 4));

        // the line, with a faint wash under it
        var pts = new Point[v.Length];
        for (int i = 0; i < v.Length; i++) pts[i] = new Point(X(i), Y(v[i]));

        if (v.Length > 1)
        {
            var area = new StreamGeometry();
            using (var c = area.Open())
            {
                c.BeginFigure(new Point(pts[0].X, bottomY), isFilled: true, isClosed: true);
                c.PolyLineTo(pts, isStroked: false, isSmoothJoin: false);
                c.LineTo(new Point(pts[^1].X, bottomY), false, false);
            }
            area.Freeze();
            var wash = Stroke.CloneCurrentValue();
            wash.Opacity = 0.12;
            wash.Freeze();
            dc.DrawGeometry(wash, null, area);

            var line = new StreamGeometry();
            using (var c = line.Open())
            {
                c.BeginFigure(pts[0], isFilled: false, isClosed: false);
                c.PolyLineTo(pts[1..], isStroked: true, isSmoothJoin: true);
            }
            line.Freeze();
            var pen = new Pen(Stroke, 2) { LineJoin = PenLineJoin.Round, StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round };
            pen.Freeze();
            dc.DrawGeometry(null, pen, line);
        }

        // session average: dashed reference with its value at the right end
        if (Average > 0)
        {
            double y = Math.Round(Y(Average)) + 0.5;
            var dash = new Pen(Label, 1) { DashStyle = new DashStyle(new[] { 3.0, 3.0 }, 0) };
            dash.Freeze();
            dc.DrawLine(dash, new Point(Left, y), new Point(w - Right, y));
            var avg = Text("avg " + Average.ToString("0", Inv), 10.5, Label, pixelsPerDip);
            double ty = y - avg.Height - 2 < Top ? y + 2 : y - avg.Height - 2;
            dc.DrawText(avg, new Point(w - Right - avg.Width, ty));
        }

        // hover: crosshair, ringed dot, readout
        if (_hover >= 0 && _hover < v.Length)
        {
            var p = pts[_hover];
            var cross = new Pen(Label, 1);
            cross.Freeze();
            dc.DrawLine(cross, new Point(Math.Round(p.X) + 0.5, Top), new Point(Math.Round(p.X) + 0.5, bottomY));
            dc.DrawEllipse(Stroke, new Pen(Surface, 2), p, 4.5, 4.5);

            string when = Clock(_hover * SecondsPerPoint);
            var readout = Text($"{when}  ·  {v[_hover].ToString("0", Inv)} fps", 11.5, TipText, pixelsPerDip);
            double bw = readout.Width + 16, bh = readout.Height + 8;
            double bx = Math.Clamp(p.X - bw / 2, 0, w - bw);
            double by = p.Y - bh - 10 < 0 ? p.Y + 10 : p.Y - bh - 10;
            dc.DrawRoundedRectangle(Tip, new Pen(Grid, 1), new Rect(bx, by, bw, bh), 6, 6);
            dc.DrawText(readout, new Point(bx + 8, by + 4));
        }
    }

    FormattedText Text(string s, double size, Brush brush, double pixelsPerDip) =>
        new(s, Inv, FlowDirection.LeftToRight, new Typeface("Segoe UI"), size, brush, pixelsPerDip);

    /// <summary>1, 2, 2.5 or 5 × 10ⁿ, at or above <paramref name="v"/>.</summary>
    static double NiceCeiling(double v)
    {
        double pow = Math.Pow(10, Math.Floor(Math.Log10(v)));
        foreach (double m in new[] { 1, 2, 2.5, 5, 10 })
            if (m * pow >= v) return m * pow;
        return 10 * pow;
    }

    static string Clock(double seconds)
    {
        var t = TimeSpan.FromSeconds(Math.Round(seconds));
        return t.TotalHours >= 1 ? $"{(int)t.TotalHours}:{t.Minutes:00}:{t.Seconds:00}" : $"{t.Minutes}:{t.Seconds:00}";
    }
}
