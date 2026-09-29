using System;
using System.Windows;
using System.Windows.Interop;

namespace Pulse;

public partial class OverlayWindow : Window
{
    public const double EdgeMargin = 16;

    IntPtr _hwnd;

    public OverlayViewModel ViewModel { get; } = new();

    /// <summary>The look on screen: the default one, or the profile of the game in front.</summary>
    public OverlayStyle Look { get; private set; }

    public OverlayWindow(OverlayStyle style)
    {
        Look = style;
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
        Card.ApplySettings(Look);
        ViewModel.ApplyStyle(Look);
        Reposition();
    }

    /// <summary>Wear another look (a game with its own profile came to the front, or left).</summary>
    public void SetLook(OverlayStyle style)
    {
        if (ReferenceEquals(style, Look)) return;
        Look = style;
        ApplySettings();
    }

    public void Reposition()
    {
        double w = ActualWidth, h = ActualHeight;
        if (w <= 0 || h <= 0) return;

        (Left, Top) = Place(Look, SystemParameters.PrimaryScreenWidth, SystemParameters.PrimaryScreenHeight, w, h);
    }

    /// <summary>Top-left of a <paramref name="w"/>×<paramref name="h"/> card for a look — shared with the preview.</summary>
    public static (double Left, double Top) Place(OverlayStyle look, double sw, double sh, double w, double h)
    {
        if (look.Corner != Corner.Custom) return Place(look.Corner, sw, sh, w, h);
        double roomX = Math.Max(0, sw - w), roomY = Math.Max(0, sh - h);
        return (Math.Round(Math.Clamp(look.CustomX, 0, 1) * roomX), Math.Round(Math.Clamp(look.CustomY, 0, 1) * roomY));
    }

    /// <summary>Top-left of a <paramref name="w"/>×<paramref name="h"/> card in one of the six spots.</summary>
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
