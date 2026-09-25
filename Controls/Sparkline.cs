using System;
using System.Windows;
using System.Windows.Media;

namespace Pulse.Controls;

/// <summary>
/// Frame-time trace. Flat line = smooth; spikes upward = stutters.
/// One StreamGeometry per redraw, frozen, twice a second at most.
/// </summary>
public sealed class Sparkline : FrameworkElement
{
    public static readonly DependencyProperty ValuesProperty = DependencyProperty.Register(
        nameof(Values), typeof(double[]), typeof(Sparkline),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty StrokeProperty = DependencyProperty.Register(
        nameof(Stroke), typeof(Brush), typeof(Sparkline),
        new FrameworkPropertyMetadata(Brushes.White, FrameworkPropertyMetadataOptions.AffectsRender, (d, _) => ((Sparkline)d)._pen = null));

    public static readonly DependencyProperty AreaProperty = DependencyProperty.Register(
        nameof(Area), typeof(Brush), typeof(Sparkline),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty BaselineProperty = DependencyProperty.Register(
        nameof(Baseline), typeof(Brush), typeof(Sparkline),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    public double[]? Values { get => (double[]?)GetValue(ValuesProperty); set => SetValue(ValuesProperty, value); }
    public Brush Stroke { get => (Brush)GetValue(StrokeProperty); set => SetValue(StrokeProperty, value); }
    public Brush? Area { get => (Brush?)GetValue(AreaProperty); set => SetValue(AreaProperty, value); }
    public Brush? Baseline { get => (Brush?)GetValue(BaselineProperty); set => SetValue(BaselineProperty, value); }

    Pen? _pen;

    protected override void OnRender(DrawingContext dc)
    {
        double w = ActualWidth, h = ActualHeight;
        if (w <= 0 || h <= 0) return;

        if (Baseline != null) dc.DrawRectangle(Baseline, null, new Rect(0, h - 1, w, 1));

        var v = Values;
        if (v is null || v.Length < 2) return;

        double max = 0, sum = 0;
        foreach (double x in v) { sum += x; if (x > max) max = x; }
        double avg = sum / v.Length;

        // Steady games sit in the lower half; real spikes reach the top.
        double top = Math.Max(Math.Max(max * 1.15, avg * 2.2), 4);
        double step = w / (v.Length - 1);
        double usable = h - 3;

        var pts = new Point[v.Length];
        for (int i = 0; i < v.Length; i++)
            pts[i] = new Point(i * step, h - 1 - Math.Min(v[i] / top, 1) * usable);

        if (Area != null)
        {
            var area = new StreamGeometry();
            using (var c = area.Open())
            {
                c.BeginFigure(new Point(0, h), isFilled: true, isClosed: true);
                c.PolyLineTo(pts, isStroked: false, isSmoothJoin: false);
                c.LineTo(new Point(w, h), false, false);
            }
            area.Freeze();
            dc.DrawGeometry(Area, null, area);
        }

        var line = new StreamGeometry();
        using (var c = line.Open())
        {
            c.BeginFigure(pts[0], isFilled: false, isClosed: false);
            c.PolyLineTo(pts[1..], isStroked: true, isSmoothJoin: true);
        }
        line.Freeze();

        if (_pen is null)
        {
            _pen = new Pen(Stroke, 1.4) { LineJoin = PenLineJoin.Round, StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round };
            if (_pen.CanFreeze) _pen.Freeze();
        }
        dc.DrawGeometry(null, _pen, line);
    }
}
