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

        // Fade only the glass, never the numbers.
        if (Card.Background is { IsFrozen: false } glass)
            glass.Opacity = Math.Clamp(settings.BackgroundOpacity, 0.2, 1.0);

        ApplyTheme(settings);
    }

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
