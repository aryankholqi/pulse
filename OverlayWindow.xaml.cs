using System;
using System.Windows;
using System.Windows.Interop;

namespace Pulse;

public partial class OverlayWindow : Window
{
    const double EdgeMargin = 16;

    readonly AppSettings _settings;
    IntPtr _hwnd;

    public OverlayViewModel ViewModel { get; } = new();

    public OverlayWindow(AppSettings settings)
    {
        _settings = settings;
        InitializeComponent();
        DataContext = ViewModel;
        SizeChanged += (_, _) => Reposition();
        ApplySettings();
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        _hwnd = new WindowInteropHelper(this).Handle;
        Native.MakeOverlayWindow(_hwnd); // click-through, no focus, no Alt+Tab
    }

    public void ApplySettings()
    {
        double scale = Math.Clamp(_settings.Scale, 0.6, 2.0);
        RootScale.ScaleX = scale;
        RootScale.ScaleY = scale;

        bool compact = _settings.Compact;
        FullPanel.Visibility = compact ? Visibility.Collapsed : Visibility.Visible;
        CompactPanel.Visibility = compact ? Visibility.Visible : Visibility.Collapsed;
        Card.Padding = compact ? new Thickness(14, 8, 14, 8) : new Thickness(16, 14, 16, 14);
        Card.CornerRadius = new CornerRadius(compact ? 12 : 16);

        // Fade only the glass, never the numbers.
        if (Card.Background is { IsFrozen: false } glass)
            glass.Opacity = Math.Clamp(_settings.BackgroundOpacity, 0.2, 1.0);

        Reposition();
    }

    public void Reposition()
    {
        double w = ActualWidth, h = ActualHeight;
        if (w <= 0 || h <= 0) return;

        double sw = SystemParameters.PrimaryScreenWidth;
        double sh = SystemParameters.PrimaryScreenHeight;

        (Left, Top) = _settings.Corner switch
        {
            Corner.TopRight => (sw - w - EdgeMargin, EdgeMargin),
            Corner.BottomLeft => (EdgeMargin, sh - h - EdgeMargin),
            Corner.BottomRight => (sw - w - EdgeMargin, sh - h - EdgeMargin),
            _ => (EdgeMargin, EdgeMargin),
        };
    }

    public void EnsureTopmost()
    {
        if (_hwnd != IntPtr.Zero) Native.SetTopmost(_hwnd);
    }
}
