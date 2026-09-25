using System;
using System.Windows;
using System.Windows.Media;

namespace Pulse.Controls;

/// <summary>A 0–100 bar drawn in one OnRender pass — no templates, no visual tree.</summary>
public sealed class MeterBar : FrameworkElement
{
    public static readonly DependencyProperty ValueProperty = DependencyProperty.Register(
        nameof(Value), typeof(double), typeof(MeterBar),
        new FrameworkPropertyMetadata(0d, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty FillProperty = DependencyProperty.Register(
        nameof(Fill), typeof(Brush), typeof(MeterBar),
        new FrameworkPropertyMetadata(Brushes.White, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty TrackProperty = DependencyProperty.Register(
        nameof(Track), typeof(Brush), typeof(MeterBar),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    public double Value { get => (double)GetValue(ValueProperty); set => SetValue(ValueProperty, value); }
    public Brush Fill { get => (Brush)GetValue(FillProperty); set => SetValue(FillProperty, value); }
    public Brush? Track { get => (Brush?)GetValue(TrackProperty); set => SetValue(TrackProperty, value); }

    protected override void OnRender(DrawingContext dc)
    {
        double w = ActualWidth, h = ActualHeight;
        if (w <= 0 || h <= 0) return;

        double r = h / 2;
        if (Track != null) dc.DrawRoundedRectangle(Track, null, new Rect(0, 0, w, h), r, r);

        double v = Math.Clamp(Value, 0, 100) / 100.0;
        if (v <= 0) return;

        double fillWidth = Math.Max(h, w * v); // never thinner than a dot
        dc.DrawRoundedRectangle(Fill, null, new Rect(0, 0, fillWidth, h), r, r);
    }
}
