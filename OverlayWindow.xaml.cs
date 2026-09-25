using System;
using System.Windows;
using System.Windows.Interop;

namespace Pulse;

public partial class OverlayWindow : Window
{
    public const double EdgeMargin = 16;

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
        Card.ApplySettings(_settings);
        ViewModel.ApplyStyle(_settings);
        Reposition();
    }

    public void Reposition()
    {
        double w = ActualWidth, h = ActualHeight;
        if (w <= 0 || h <= 0) return;

        (Left, Top) = Place(_settings.Corner, SystemParameters.PrimaryScreenWidth, SystemParameters.PrimaryScreenHeight, w, h);
    }

    /// <summary>Top-left of a <paramref name="w"/>×<paramref name="h"/> card on a screen — shared with the preview.</summary>
    public static (double Left, double Top) Place(Corner corner, double sw, double sh, double w, double h)
    {
        double left = EdgeMargin, center = Math.Round((sw - w) / 2), right = sw - w - EdgeMargin;
        double top = EdgeMargin, bottom = sh - h - EdgeMargin;

        return corner switch
        {
            Corner.TopCenter => (center, top),
            Corner.TopRight => (right, top),
            Corner.BottomLeft => (left, bottom),
            Corner.BottomCenter => (center, bottom),
            Corner.BottomRight => (right, bottom),
            _ => (left, top),
        };
    }

    public void EnsureTopmost()
    {
        if (_hwnd != IntPtr.Zero) Native.SetTopmost(_hwnd);
    }
}
