using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace Pulse;

/// <summary>
/// The overlay's instrument card. Hosted by the click-through <see cref="OverlayWindow"/>
/// and, identically, by the preview in <see cref="SettingsWindow"/>.
/// </summary>
public partial class OverlayCard : UserControl
{
    public OverlayCard()
    {
        InitializeComponent();
    }

    /// <summary>Layout, size, glass opacity and colours from settings.</summary>
    public void ApplySettings(AppSettings settings)
    {
        double scale = Math.Clamp(settings.Scale, 0.6, 2.0);
        RootScale.ScaleX = scale;
        RootScale.ScaleY = scale;

        bool compact = settings.Compact;
        FullPanel.Visibility = compact ? Visibility.Collapsed : Visibility.Visible;
        CompactPanel.Visibility = compact ? Visibility.Visible : Visibility.Collapsed;
        Card.Padding = compact ? new Thickness(14, 8, 14, 8) : new Thickness(16, 14, 16, 14);
        Card.CornerRadius = new CornerRadius(compact ? 12 : 16);

        ApplyMetrics(settings);

        // Fade only the glass, never the numbers.
        if (Card.Background is { IsFrozen: false } glass)
            glass.Opacity = Math.Clamp(settings.BackgroundOpacity, 0.2, 1.0);

        ApplyTheme(settings);
    }

    /// <summary>Show only the metrics the user picked, with dividers and gaps only between visible ones.</summary>
    void ApplyMetrics(AppSettings s)
    {
        Show(FullFps, s.ShowFps);
        Show(CompactFps, s.ShowFps);

        // (full row, compact group, visible) in display order
        var devices = new (FrameworkElement Full, Panel Compact, bool On)[]
        {
            (FullGpu, CompactGpu, s.ShowGpu),
            (FullCpu, CompactCpu, s.ShowCpu),
            (FullRam, CompactRam, s.ShowRam),
        };

        bool compactLead = !s.ShowFps, fullLead = true;
        foreach (var (full, compact, on) in devices)
        {
            Show(full, on);
            Show(compact, on);
            if (!on) continue;

            full.Margin = new Thickness(0, fullLead ? 0 : 10, 0, 0);
            compact.Children[0].Visibility = compactLead ? Visibility.Collapsed : Visibility.Visible; // its divider
            fullLead = compactLead = false;
        }
    }

    static void Show(UIElement element, bool on) =>
        element.Visibility = on ? Visibility.Visible : Visibility.Collapsed;

    void ApplyTheme(AppSettings s)
    {
        SetHue("Gpu", ColorUtil.Parse(s.GpuColor, AppSettings.DefaultGpuColor));
        SetHue("Cpu", ColorUtil.Parse(s.CpuColor, AppSettings.DefaultCpuColor));
        SetHue("Ram", ColorUtil.Parse(s.RamColor, AppSettings.DefaultRamColor));

        // The frame-time trace wears the FPS colour.
        Color fps = ColorUtil.Parse(s.FpsColor, AppSettings.DefaultFpsColor);
        Resources["TraceStroke"] = ColorUtil.Solid(fps);
        Resources["TraceArea"] = ColorUtil.Frozen(new LinearGradientBrush(
            Color.FromArgb(0x2E, fps.R, fps.G, fps.B), Color.FromArgb(0, fps.R, fps.G, fps.B), 90));
    }

    void SetHue(string device, Color hue)
    {
        Resources[device + "Hue"] = ColorUtil.Solid(hue);
        Resources[device + "Fill"] = ColorUtil.Frozen(new LinearGradientBrush(
            ColorUtil.Mix(hue, Colors.Black, 0.28), ColorUtil.Mix(hue, Colors.White, 0.2), 0));
    }
}
