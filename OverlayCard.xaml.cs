using System;
using System.Collections.Generic;
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
    /// <summary>Only with no background: a tight dark halo, the way on-screen displays stay readable anywhere.</summary>
    static readonly System.Windows.Media.Effects.DropShadowEffect Legible = MakeLegible();

    static System.Windows.Media.Effects.DropShadowEffect MakeLegible()
    {
        var e = new System.Windows.Media.Effects.DropShadowEffect
        {
            Color = Colors.Black,
            ShadowDepth = 0,
            BlurRadius = 3,
            Opacity = 1,
            RenderingBias = System.Windows.Media.Effects.RenderingBias.Performance,
        };
        e.Freeze();
        return e;
    }

    /// <summary>
    /// Liquid glass: a clear, cool tint over the blurred game, dark enough to keep ivory text readable on a
    /// bright scene. The smoked glass (from the XAML) is kept for when it's off.
    /// </summary>
    readonly Brush _smoked;
    readonly LinearGradientBrush _liquid = new(new GradientStopCollection
    {
        new GradientStop(Color.FromArgb(0x70, 0x22, 0x28, 0x3E), 0),
        new GradientStop(Color.FromArgb(0x80, 0x12, 0x16, 0x24), 1),
    }, new Point(0, 0), new Point(0.35, 1));

    public OverlayCard()
    {
        InitializeComponent();
        _smoked = Card.Background;
        LayoutUpdated += (_, _) => FitBackdrop();
    }

    /// <summary>The look asks for liquid glass and has a background to show it on.</summary>
    public bool LiquidGlass { get; private set; }

    /// <summary>
    /// For a card that isn't its own window (the settings preview): what lies behind it, blurred under liquid glass.
    /// Must not be an ancestor of the card.
    /// </summary>
    public Visual? BackdropSource
    {
        get => _backdropSource;
        set
        {
            _backdropSource = value;
            BackdropFill.Fill = value is null ? null : new VisualBrush(value) { ViewboxUnits = BrushMappingMode.Absolute, Stretch = Stretch.Fill };
            _backdropView = Rect.Empty;
            ShowBackdrop();
        }
    }

    Visual? _backdropSource;
    Rect _backdropView = Rect.Empty;
    const double LiquidCorner = 8; // DWM's round window corner, in DIPs (Native.SetRoundCorners)
    const double BackdropBleed = 24; // past the card on every side: the blur's soft edge falls outside the clip

    void ShowBackdrop() =>
        Backdrop.Visibility = LiquidGlass && _backdropSource is not null ? Visibility.Visible : Visibility.Collapsed;

    /// <summary>Line the blurred copy up with what's really behind the card. Cheap when nothing moved.</summary>
    void FitBackdrop()
    {
        if (Backdrop.Visibility != Visibility.Visible || _backdropSource is null || BackdropFill.Fill is not VisualBrush brush) return;
        double w = Backdrop.ActualWidth, h = Backdrop.ActualHeight;
        if (w <= 0 || h <= 0) return;
        try
        {
            var area = new Rect(-BackdropBleed, -BackdropBleed, w + 2 * BackdropBleed, h + 2 * BackdropBleed);
            var view = Backdrop.TransformToVisual(_backdropSource).TransformBounds(area);
            if (view == _backdropView) return;
            _backdropView = view;
            brush.Viewbox = view;
            BackdropFill.Margin = new Thickness(-BackdropBleed);
            Backdrop.Clip = new RectangleGeometry(new Rect(0, 0, w, h), Card.CornerRadius.TopLeft, Card.CornerRadius.TopLeft);
        }
        catch (InvalidOperationException) { } // not in the same tree (yet)
    }

    /// <summary>Layout, size, glass opacity and colours from settings.</summary>
    public void ApplySettings(OverlayStyle settings)
    {
        double scale = Math.Clamp(settings.Scale, 0.6, 2.0);
        RootScale.ScaleX = scale;
        RootScale.ScaleY = scale;

        bool compact = settings.Compact;
        FullPanel.Visibility = compact ? Visibility.Collapsed : Visibility.Visible;
        CompactPanel.Visibility = compact ? Visibility.Visible : Visibility.Collapsed;
        Card.Padding = compact ? new Thickness(14, 8, 14, 8) : new Thickness(16, 14, 16, 14);

        ApplyMetrics(settings);

        // Fade only the glass, never the numbers. At 0 the card goes entirely: no glass, no rim,
        // and a soft shadow under the figures keeps them readable on a bright scene.
        double opacity = Math.Clamp(settings.BackgroundOpacity, 0, 1.0);
        bool bare = opacity < 0.01;
        bool liquid = settings.LiquidGlass && !bare;
        LiquidGlass = liquid;

        // Windows rounds the live overlay's blur at a fixed 8 DIPs, whatever the size: liquid glass's card follows it.
        Card.CornerRadius = new CornerRadius(liquid ? LiquidCorner / scale : compact ? 12 : 16);

        Card.Background = liquid ? _liquid : _smoked;
        if (Card.Background is { IsFrozen: false } glass) glass.Opacity = opacity;
        Card.BorderThickness = new Thickness(bare || liquid ? 0 : 1); // liquid glass wears the sheen's rim instead
        Sheen.Visibility = liquid ? Visibility.Visible : Visibility.Collapsed;
        CatchLight.Visibility = liquid ? Visibility.Collapsed : Visibility.Visible; // liquid glass lights only its corners
        SheenTopLeft.CornerRadius = SheenBottomRight.CornerRadius = Card.CornerRadius;
        Sheen.Opacity = 0.55 + 0.45 * opacity; // the rim fades with the glass, but never away
        _backdropView = Rect.Empty; // the corners may have changed
        ShowBackdrop();

        Figures.Effect = bare ? Legible : null;
        Card.Effect = bare ? Legible : null; // a second pass: one soft halo is too faint around thin text
        ApplyText(settings, bare || liquid); // the game shows through liquid glass: the quiet shades lighten as with none

        ApplyTheme(settings);
    }

    /// <summary>Show only the metrics the user picked, with dividers and gaps only between visible ones.</summary>
    void ApplyMetrics(OverlayStyle s)
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

    // The stock size of every text role (see the card's resources), scaled by the user's size.
    static readonly (string Key, double Size)[] NumberSizes =
        [("NumHero", 44), ("NumCompactHero", 18), ("NumLow", 17), ("Num", 16), ("NumHotspot", 13), ("NumSmall", 12)];
    static readonly (string Key, double Size)[] LabelSizes =
        [("LblDevice", 11), ("LblUnit", 12), ("LblNote", 10)];
    const double FullWidth = 238;

    static readonly Color Glass = Color.FromRgb(0x17, 0x1B, 0x2E); // the glass under the text, for quieter shades

    /// <summary>
    /// Sizes and colours of the numbers and the labels. The quiet shades (secondary numbers, notes,
    /// meter tracks, rules) are tuned for the dark glass; with no glass, or liquid glass the game shows through,
    /// they'd vanish into a bright scene, so they lighten. The brushes are shared by the card's styles: recolouring them is enough.
    /// </summary>
    void ApplyText(OverlayStyle s, bool seeThrough)
    {
        double num = Math.Clamp(s.NumberSize, OverlayStyle.MinTextSize, OverlayStyle.MaxTextSize);
        double lbl = Math.Clamp(s.LabelSize, OverlayStyle.MinTextSize, OverlayStyle.MaxTextSize);
        foreach (var (key, size) in NumberSizes) Put(key, Math.Round(size * num, 1));
        foreach (var (key, size) in LabelSizes) Put(key, Math.Round(size * lbl, 1));
        Put("Divider", Math.Round(14 * Math.Max(num, lbl)));
        FullPanel.Width = Math.Round(FullWidth * Math.Max(num, lbl)); // the columns take what the text needs; the meters share the rest

        // Numbers: the main figures in the colour, the secondary ones (loads, 1% low) a step quieter.
        var ink = ColorUtil.Parse(s.NumberColor, OverlayStyle.DefaultNumberColor);
        bool stockInk = ink == ColorUtil.Parse(OverlayStyle.DefaultNumberColor, OverlayStyle.DefaultNumberColor);
        Tint("Ink", ink);
        Tint("NumSoft", stockInk ? Stock(seeThrough ? 0xE6E9F2 : 0xA3AAC2) : seeThrough ? ink : ColorUtil.Mix(ink, Glass, 0.3));

        // Labels: names and units in the colour, the smallest notes a step quieter.
        var label = ColorUtil.Parse(s.LabelColor, OverlayStyle.DefaultLabelColor);
        bool stockLabel = label == ColorUtil.Parse(OverlayStyle.DefaultLabelColor, OverlayStyle.DefaultLabelColor);
        Tint("Soft", stockLabel ? Stock(seeThrough ? 0xE6E9F2 : 0xA3AAC2) : label);
        Tint("Faint", stockLabel ? Stock(seeThrough ? 0xB9BFD1 : 0x5E6680)
                                 : ColorUtil.Mix(label, seeThrough ? Colors.Gray : Glass, seeThrough ? 0.2 : 0.4));

        Tint("TrackBrush", Color.FromArgb((byte)(seeThrough ? 0x40 : 0x1C), 0xFF, 0xFF, 0xFF));
        Tint("RuleBrush", Color.FromArgb((byte)(seeThrough ? 0x30 : 0x14), 0xFF, 0xFF, 0xFF));
    }

    static Color Stock(int rgb) => Color.FromRgb((byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb);

    void Tint(string key, Color color)
    {
        if (Resources[key] is SolidColorBrush { IsFrozen: false } brush && brush.Color != color) brush.Color = color;
    }

    /// <summary>
    /// Replace a resource only when its value really changes: every replacement makes WPF re-resolve the
    /// resource through the whole card, and one colour click shouldn't redo all twenty of them.
    /// </summary>
    void Put(string key, object value)
    {
        if (!Equals(Resources[key], value)) Resources[key] = value;
    }

    static void Show(UIElement element, bool on) =>
        element.Visibility = on ? Visibility.Visible : Visibility.Collapsed;

    // The last colours put in the resources, so an unchanged one isn't rebuilt (see Put).
    readonly Dictionary<string, Color> _hues = new();

    void ApplyTheme(OverlayStyle s)
    {
        SetHue("Gpu", ColorUtil.Parse(s.GpuColor, AppSettings.DefaultGpuColor));
        SetHue("Cpu", ColorUtil.Parse(s.CpuColor, AppSettings.DefaultCpuColor));
        SetHue("Ram", ColorUtil.Parse(s.RamColor, AppSettings.DefaultRamColor));

        // The frame-time trace wears the FPS colour.
        Color fps = ColorUtil.Parse(s.FpsColor, AppSettings.DefaultFpsColor);
        if (!Changed("Trace", fps)) return;
        Resources["TraceStroke"] = ColorUtil.Solid(fps);
        Resources["TraceArea"] = ColorUtil.Frozen(new LinearGradientBrush(
            Color.FromArgb(0x2E, fps.R, fps.G, fps.B), Color.FromArgb(0, fps.R, fps.G, fps.B), 90));
    }

    bool Changed(string key, Color color)
    {
        if (_hues.TryGetValue(key, out var last) && last == color) return false;
        _hues[key] = color;
        return true;
    }

    void SetHue(string device, Color hue)
    {
        if (!Changed(device, hue)) return;
        Resources[device + "Hue"] = ColorUtil.Solid(hue);
        Resources[device + "Fill"] = ColorUtil.Frozen(new LinearGradientBrush(
            ColorUtil.Mix(hue, Colors.Black, 0.28), ColorUtil.Mix(hue, Colors.White, 0.2), 0));
    }
}
