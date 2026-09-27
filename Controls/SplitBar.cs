using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace Pulse.Controls;

/// <summary>
/// Parts of a whole as one bar: a rounded segment per part, 2 px apart. A hatched segment reads
/// apart from its neighbours even without colour. Hovering a segment shows its label.
/// </summary>
public sealed class SplitBar : FrameworkElement
{
    public sealed record Part(double Share, Brush Fill, bool Hatched, string Label);

    const double Gap = 2;

    IReadOnlyList<Part> _parts = Array.Empty<Part>();
    readonly List<(Rect Rect, Part Part)> _drawn = new();

    public IReadOnlyList<Part> Parts
    {
        get => _parts;
        set { _parts = value; InvalidateVisual(); }
    }

    public SplitBar()
    {
        FlowDirection = FlowDirection.LeftToRight; // matches the legend order in both languages
        ToolTipService.SetInitialShowDelay(this, 150);
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        var at = e.GetPosition(this);
        foreach (var (rect, part) in _drawn)
            if (at.X >= rect.Left - Gap / 2 && at.X <= rect.Right + Gap / 2)
            {
                if (!Equals(ToolTip, part.Label)) ToolTip = part.Label;
                return;
            }
    }

    protected override void OnRender(DrawingContext dc)
    {
        _drawn.Clear();
        double w = ActualWidth, h = ActualHeight;
        if (w <= 0 || h <= 0) return;

        // hit target: the whole bar height
        dc.DrawRectangle(Brushes.Transparent, null, new Rect(0, 0, w, h));

        double total = 0;
        int visible = 0;
        foreach (var p in _parts) if (p.Share > 0) { total += p.Share; visible++; }
        if (total <= 0) return;

        double usable = w - Gap * (visible - 1);
        double x = 0, r = Math.Min(4, h / 2);
        foreach (var p in _parts)
        {
            if (p.Share <= 0) continue;
            double width = Math.Max(h, usable * p.Share / total); // never thinner than a dot
            var rect = new Rect(x, 0, Math.Min(width, w - x), h);
            if (rect.Width <= 0) break;

            if (p.Hatched)
            {
                var wash = p.Fill.CloneCurrentValue();
                wash.Opacity = 0.35;
                wash.Freeze();
                dc.DrawRoundedRectangle(wash, null, rect, r, r);
                dc.PushClip(new RectangleGeometry(rect, r, r));
                dc.DrawRectangle(Hatch(p.Fill), null, rect);
                dc.Pop();
            }
            else
            {
                dc.DrawRoundedRectangle(p.Fill, null, rect, r, r);
            }

            _drawn.Add((rect, p));
            x += width + Gap;
        }
    }

    /// <summary>45° stripes in the part's own colour.</summary>
    static Brush Hatch(Brush color)
    {
        var pen = new Pen(color, 1.5);
        pen.Freeze();
        var stripe = new GeometryDrawing(null, pen, new LineGeometry(new Point(0, 6), new Point(6, 0)));
        var brush = new DrawingBrush(stripe)
        {
            TileMode = TileMode.Tile,
            Viewport = new Rect(0, 0, 6, 6),
            ViewportUnits = BrushMappingMode.Absolute,
            Viewbox = new Rect(0, 0, 6, 6),
            ViewboxUnits = BrushMappingMode.Absolute,
        };
        brush.Freeze();
        return brush;
    }
}
