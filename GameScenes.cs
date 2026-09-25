using System;
using System.Collections.Generic;
using System.Globalization;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Pulse;

/// <summary>
/// Backdrops for the overlay preview. Every scene is painted from code — an evocation
/// of each game's look (palette, light, skyline, HUD layout), no copyrighted art shipped.
/// Users who want the real thing load their own screenshot.
/// </summary>
internal sealed class GameScene
{
    public required string Id { get; init; }
    public required string Title { get; init; }
    public required string Place { get; init; }

    /// <summary>Accent used for the scene's chip in the picker.</summary>
    public required Color Accent { get; init; }

    /// <summary>Typical frame rate for the simulated preview numbers.</summary>
    public required double Fps { get; init; }
    public required float GpuLoad { get; init; }
    public required float CpuLoad { get; init; }

    public required Action<DrawingContext, Random> Paint { get; init; }
    public required Action<DrawingContext, double, double> Hud { get; init; }

    public const double W = 1920, H = 1080;

    /// <summary>The scene painted into a bitmap, so the preview never repaints vectors. Not cached here:
    /// the settings window holds it only while open, and the memory goes when it closes.</summary>
    public ImageSource RenderImage() => Render((int)W, (int)H, dc => Paint(dc, new Random(Seed(Id))));

    // string.GetHashCode is randomised per process; scenes must look the same every launch.
    static int Seed(string s)
    {
        int h = 17;
        foreach (char c in s) h = h * 31 + c;
        return h & 0x7FFFFFFF;
    }

    /// <summary>HUD sized to the real screen so its corners line up with the overlay's.</summary>
    public ImageSource RenderHud(double w, double h) => Render((int)Math.Round(w), (int)Math.Round(h), dc => Hud(dc, w, h));

    static ImageSource Render(int w, int h, Action<DrawingContext> draw)
    {
        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen()) draw(dc);
        var bmp = new RenderTargetBitmap(Math.Max(w, 1), Math.Max(h, 1), 96, 96, PixelFormats.Pbgra32);
        bmp.Render(visual);
        bmp.Freeze();
        return bmp;
    }
}

internal static class GameScenes
{
    public static readonly IReadOnlyList<GameScene> All = new[]
    {
        new GameScene
        {
            Id = "tlou1", Title = "The Last of Us Part I", Place = "Boston · overgrown downtown",
            Accent = C("#8FA86A"), Fps = 94, GpuLoad = 97, CpuLoad = 58,
            Paint = PaintTlou1, Hud = HudTlou,
        },
        new GameScene
        {
            Id = "tlou2", Title = "The Last of Us Part II", Place = "Seattle · rain",
            Accent = C("#6FA3A8"), Fps = 88, GpuLoad = 98, CpuLoad = 52,
            Paint = PaintTlou2, Hud = HudTlou,
        },
        new GameScene
        {
            Id = "cyberpunk", Title = "Cyberpunk 2077", Place = "Night City · neon",
            Accent = C("#FCEE0A"), Fps = 71, GpuLoad = 99, CpuLoad = 66,
            Paint = PaintCyberpunk, Hud = HudCyberpunk,
        },
        new GameScene
        {
            Id = "elden", Title = "Elden Ring", Place = "The Lands Between · dusk",
            Accent = C("#E8B560"), Fps = 60, GpuLoad = 81, CpuLoad = 47,
            Paint = PaintElden, Hud = HudElden,
        },
        new GameScene
        {
            Id = "rdr2", Title = "Red Dead Redemption 2", Place = "The frontier · sunset",
            Accent = C("#E0703F"), Fps = 83, GpuLoad = 96, CpuLoad = 61,
            Paint = PaintRdr2, Hud = HudRdr2,
        },
        new GameScene
        {
            Id = "gow", Title = "God of War Ragnarök", Place = "Midgard · Fimbulwinter",
            Accent = C("#A9C6DA"), Fps = 102, GpuLoad = 95, CpuLoad = 44,
            Paint = PaintGow, Hud = HudGow,
        },
        new GameScene
        {
            Id = "cs2", Title = "Counter-Strike 2", Place = "Dust II · long A",
            Accent = C("#D8B27A"), Fps = 287, GpuLoad = 74, CpuLoad = 71,
            Paint = PaintCs2, Hud = HudCs2,
        },
    };

    public static GameScene Find(string? id)
    {
        foreach (var s in All) if (s.Id == id) return s;
        return All[0];
    }

    const double W = GameScene.W, H = GameScene.H;

    // ═════════════════════════════ scenes ═════════════════════════════

    static void PaintTlou1(DrawingContext dc, Random r)
    {
        Sky(dc, ("#8E9A8C", 0), ("#CFCDB3", 0.55), ("#E7E0C4", 0.72));
        Glow(dc, 1380, 250, 520, C("#FFF3D0"), 0.85);
        Rays(dc, r, 1380, 250, C("#FFF6DC"), 7, 0.10);

        Skyline(dc, r, 760, 260, 560, 60, 150, Solid("#A3A792"), broken: 0.6, gap: 8);
        Fog(dc, 560, 800, C("#E2DCC2"), 0.55);
        var mid = Skyline(dc, r, 800, 140, 420, 90, 190, Solid("#5D6753"), broken: 0.7, gap: 30);
        Vines(dc, r, mid, C("#4F6B3A"), C("#6F8B4A"));
        Windows(dc, r, mid, Solid("#434B3C"), 0.55);

        // a gutted tower filling the left edge, moss running down its face
        var tower = new Rect(-40, 90, 430, 900);
        dc.DrawRectangle(Solid("#343C2F"), null, tower);
        Windows(dc, r, new[] { tower }, Solid("#232A20"), 0.8, 26, 40);
        Vines(dc, r, new[] { tower }, C("#3F5A2E"), C("#5E7C3F"), 26);

        Ground(dc, 800, "#4A5638", "#1F271B");
        Grass(dc, r, 900, 1080, C("#6E8A45"), C("#2B3822"), 900);
        Vignette(dc, 0.55);
    }

    static void PaintTlou2(DrawingContext dc, Random r)
    {
        Sky(dc, ("#56686D", 0), ("#8A9C9B", 0.5), ("#AEBBB5", 0.7));
        Fog(dc, 300, 700, C("#C4CFCA"), 0.25);

        var far = Skyline(dc, r, 740, 200, 460, 60, 140, Solid("#7F8F8F"), broken: 0.3, gap: 10);
        // needle tower on the skyline
        var needle = Solid("#6D7D7E");
        dc.DrawRectangle(needle, null, new Rect(1492, 330, 10, 420));
        dc.DrawGeometry(needle, null, Poly((1440, 330), (1554, 330), (1530, 352), (1464, 352)));
        dc.DrawRectangle(needle, null, new Rect(1496, 270, 2, 60));

        Fog(dc, 520, 780, C("#A9B7B2"), 0.6);
        var mid = Skyline(dc, r, 790, 120, 380, 110, 220, Solid("#3C4A4D"), broken: 0.6, gap: 34);
        Windows(dc, r, mid, Solid("#2C383B"), 0.6);
        Vines(dc, r, mid, C("#2F4A3A"), C("#46604A"), 10);

        // flooded street: sky and towers mirrored in dark water
        dc.DrawRectangle(VGrad(("#6E7F80", 0), ("#1B2426", 1)), null, new Rect(0, 790, W, H - 790));
        for (int i = 0; i < 70; i++)
        {
            double y = 800 + r.NextDouble() * 280, x = r.NextDouble() * W;
            dc.DrawRectangle(Solid(Color.FromArgb((byte)r.Next(20, 60), 0xD8, 0xE4, 0xE0)), null, new Rect(x, y, 40 + r.NextDouble() * 240, 1.5));
        }

        // an overgrown tree on the right
        Canopy(dc, r, 1720, 260, 360, C("#1C2A24"), C("#2B3E33"));
        dc.DrawRectangle(Solid("#1A2320"), null, new Rect(1740, 420, 34, 660));

        Rain(dc, r, 900, C("#DCE7E5"));
        Vignette(dc, 0.6);
    }

    static void PaintCyberpunk(DrawingContext dc, Random r)
    {
        Sky(dc, ("#07061A", 0), ("#1F0C35", 0.45), ("#6C1B53", 0.78), ("#B0336A", 0.9));
        Glow(dc, 960, 820, 900, C("#FF3C8E"), 0.35);

        var far = Skyline(dc, r, 860, 300, 760, 70, 150, Solid("#1B1735"), broken: 0, gap: 6);
        Lights(dc, r, far, new[] { C("#46E6FF"), C("#FCEE0A"), C("#FF9BD0") }, 0.10);
        Fog(dc, 600, 900, C("#8C2A6A"), 0.35);

        var near = Skyline(dc, r, 900, 200, 620, 140, 260, Solid("#100D22"), broken: 0, gap: 40);
        Lights(dc, r, near, new[] { C("#46E6FF"), C("#FCEE0A") }, 0.06);
        Neon(dc, r, near, new[] { C("#FF2E88"), C("#2EF2FF"), C("#FCEE0A"), C("#B45CFF") });

        // wet street: neon smeared into long reflections
        dc.DrawRectangle(Solid("#08070F"), null, new Rect(0, 900, W, H - 900));
        foreach (var b in near)
        {
            if (r.NextDouble() < 0.5) continue;
            var c = new[] { C("#FF2E88"), C("#2EF2FF"), C("#FCEE0A") }[r.Next(3)];
            dc.DrawRectangle(VGrad((Hex(c, 0x55), 0), (Hex(c, 0), 1)), null, new Rect(b.X + b.Width * 0.3, 900, 18 + r.NextDouble() * 30, 180));
        }
        Vignette(dc, 0.5);
    }

    static void PaintElden(DrawingContext dc, Random r)
    {
        Sky(dc, ("#262436", 0), ("#6B5147", 0.42), ("#D59A55", 0.66), ("#F4CD84", 0.74));

        // the great golden tree
        Glow(dc, 1240, 330, 760, C("#FFD886"), 0.75);
        dc.DrawGeometry(VGrad(("#FFE3A0", 0), ("#D9A24E", 1)), null,
            Poly((1222, 740), (1258, 740), (1250, 330), (1232, 330)));
        for (int i = 0; i < 90; i++)
        {
            double a = r.NextDouble() * Math.PI, d = 60 + r.NextDouble() * 330;
            double x = 1240 + Math.Cos(a) * d * 1.25, y = 330 - Math.Sin(a) * d * 0.62;
            double s = 30 + r.NextDouble() * 70;
            dc.DrawEllipse(Solid(Color.FromArgb((byte)r.Next(30, 90), 0xFF, 0xD5, 0x7A)), null, new Point(x, y), s, s * 0.6);
        }
        Glow(dc, 1240, 330, 260, C("#FFF1C4"), 0.6);

        Ridge(dc, r, 740, 60, Solid("#6F6154"), 18);
        Fog(dc, 600, 780, C("#E9BD78"), 0.45);
        Ridge(dc, r, 800, 90, Solid("#3E362D"), 26);

        // a keep on a cliff, left
        var keep = Solid("#231F1C");
        dc.DrawGeometry(keep, null, Poly((0, 1080), (0, 520), (220, 560), (420, 700), (620, 820), (760, 1080)));
        foreach (var (x, y, w, h) in new[] { (60.0, 330.0, 70.0, 230.0), (160.0, 250.0, 56.0, 320.0), (250.0, 380.0, 90.0, 200.0), (360.0, 470.0, 60.0, 170.0) })
        {
            dc.DrawRectangle(keep, null, new Rect(x, y, w, h));
            dc.DrawGeometry(keep, null, Poly((x - 8, y), (x + w / 2, y - w * 1.1), (x + w + 8, y)));
        }

        Ground(dc, 880, "#3B3423", "#15120C");
        Grass(dc, r, 930, 1080, C("#7A6A3A"), C("#221D12"), 500);
        for (int i = 0; i < 70; i++) // drifting motes of grace
            Glow(dc, r.NextDouble() * W, 200 + r.NextDouble() * 800, 3 + r.NextDouble() * 6, C("#FFE2A0"), 0.9);
        Vignette(dc, 0.6);
    }

    static void PaintRdr2(DrawingContext dc, Random r)
    {
        Sky(dc, ("#2E2340", 0), ("#8E3E45", 0.4), ("#E0703F", 0.62), ("#F5B16A", 0.72));
        Glow(dc, 900, 690, 700, C("#FFC27A"), 0.7);
        dc.DrawEllipse(Solid("#FFE3B0"), null, new Point(900, 690), 70, 70);

        Ridge(dc, r, 700, 120, Solid("#8B4B4C"), 14);
        Ridge(dc, r, 745, 80, Solid("#6A3540"), 18);

        // mesas and buttes
        var mesa = Solid("#3E2230");
        dc.DrawGeometry(mesa, null, Poly((120, 790), (190, 560), (470, 540), (540, 790)));
        dc.DrawGeometry(mesa, null, Poly((1260, 790), (1300, 610), (1390, 600), (1420, 790)));
        dc.DrawGeometry(mesa, null, Poly((1500, 790), (1560, 500), (1830, 490), (1920, 640), (1920, 790)));

        Ground(dc, 780, "#4A2A28", "#150C0E");

        // fence and a lone pine line, backlit
        var ink = Solid("#120A0C");
        for (int i = 0; i < 16; i++)
        {
            double x = 80 + i * 70, y = 880 + i * 10;
            dc.DrawRectangle(ink, null, new Rect(x, y - 70, 7, 70));
        }
        dc.DrawGeometry(null, new Pen(ink, 4), Poly((80, 835), (1200, 985)));
        for (int i = 0; i < 6; i++) Pine(dc, 1500 + i * 70 + r.Next(-20, 20), 1080, 380 + r.Next(0, 160), ink);
        Grass(dc, r, 960, 1080, C("#4B2A22"), C("#120A0C"), 600);
        Vignette(dc, 0.55);
    }

    static void PaintGow(DrawingContext dc, Random r)
    {
        Sky(dc, ("#8FA6BA", 0), ("#CAD7E2", 0.5), ("#EEF3F7", 0.68));
        Glow(dc, 700, 300, 600, Colors.White, 0.6);

        var peaks = VGrad(("#FFFFFF", 0), ("#A9BCCB", 0.5), ("#6F879B", 1));
        Ridge(dc, r, 720, 380, peaks, 9, jag: 0.9);
        Fog(dc, 520, 760, C("#E7EEF3"), 0.7);
        Ridge(dc, r, 760, 180, VGrad(("#F4F8FB", 0), ("#8FA5B6", 1)), 14, jag: 0.7);

        // frozen lake with cracks
        dc.DrawRectangle(VGrad(("#D5E3EC", 0), ("#9EB6C6", 1)), null, new Rect(0, 760, W, H - 760));
        var crack = new Pen(Solid(Color.FromArgb(0x70, 0x6C, 0x88, 0x9C)), 1.4);
        for (int i = 0; i < 22; i++)
        {
            double x = r.NextDouble() * W, y = 780 + r.NextDouble() * 280;
            var pts = new List<Point> { new(x, y) };
            for (int k = 0; k < 5; k++) pts.Add(new Point(x += r.Next(-90, 90), y += r.Next(-6, 26)));
            dc.DrawGeometry(null, crack, Poly(pts.ToArray()));
        }

        // snowy pines framing the left
        var pine = Solid("#26343F");
        for (int i = 0; i < 7; i++) Pine(dc, -30 + i * 80 + r.Next(-20, 20), 1080, 520 + r.Next(0, 300), pine, snow: true);
        Snow(dc, r, 900);
        Vignette(dc, 0.35);
    }

    static void PaintCs2(DrawingContext dc, Random r)
    {
        Sky(dc, ("#5E9BD3", 0), ("#A9CDEB", 0.4));
        Glow(dc, 1500, 120, 500, C("#FFFBEA"), 0.6);

        // corridor in one-point perspective toward an archway
        var lit = HGrad(("#E6C58E", 0), ("#D2AA6C", 1));
        var shade = HGrad(("#A57E4B", 0), ("#C29760", 1));
        dc.DrawGeometry(shade, null, Poly((0, 0), (700, 300), (700, 760), (0, 1080)));
        dc.DrawGeometry(lit, null, Poly((1920, 120), (1240, 330), (1240, 760), (1920, 1080)));
        dc.DrawRectangle(Solid("#DDBB84"), null, new Rect(700, 300, 540, 460));
        // arch
        var arch = new StreamGeometry();
        using (var g = arch.Open())
        {
            g.BeginFigure(new Point(860, 760), true, true);
            g.LineTo(new Point(860, 520), false, false);
            g.ArcTo(new Point(1080, 520), new Size(110, 110), 0, false, SweepDirection.Clockwise, false, false);
            g.LineTo(new Point(1080, 760), false, false);
        }
        dc.DrawGeometry(VGrad(("#8FB8DD", 0), ("#D8C7A0", 1)), null, arch);
        // stone courses
        var course = new Pen(Solid(Color.FromArgb(0x30, 0x6A, 0x4A, 0x22)), 2);
        for (int i = 1; i < 9; i++)
        {
            dc.DrawLine(course, new Point(0, i * 120), new Point(700, 300 + i * 51));
            dc.DrawLine(course, new Point(1920, 120 + i * 107), new Point(1240, 330 + i * 48));
        }
        // floor
        dc.DrawGeometry(VGrad(("#CDB083", 0), ("#B89160", 1)), null, Poly((0, 1080), (700, 760), (1240, 760), (1920, 1080)));
        // crates
        Crate(dc, 1340, 700, 250);
        Crate(dc, 1560, 780, 300);
        Crate(dc, 1470, 560, 170);
        Vignette(dc, 0.3);
    }

    // ═════════════════════════════ HUDs ═════════════════════════════

    static void HudTlou(DrawingContext dc, double w, double h)
    {
        var ink = Solid(Color.FromArgb(0xE6, 0xF2, 0xEE, 0xE2));
        var faint = Solid(Color.FromArgb(0x55, 0x00, 0x00, 0x00));
        double x = w - 360, y = h - 130;
        dc.DrawRoundedRectangle(faint, null, new Rect(x, y, 300, 70), 6, 6);
        dc.DrawRectangle(Solid(Color.FromArgb(0x40, 255, 255, 255)), null, new Rect(x + 16, y + 46, 268, 8));
        dc.DrawRectangle(ink, null, new Rect(x + 16, y + 46, 190, 8));
        Text(dc, "12", x + 18, y + 6, 30, ink, bold: true);
        Text(dc, "| 36", x + 62, y + 14, 20, Solid(Color.FromArgb(0xA0, 0xF2, 0xEE, 0xE2)));
        dc.DrawRoundedRectangle(null, new Pen(ink, 2), new Rect(x + 200, y + 10, 80, 26), 3, 3);
    }

    static void HudCyberpunk(DrawingContext dc, double w, double h)
    {
        var yellow = Solid("#FCEE0A");
        var cyan = Solid("#46E6FF");
        var red = Solid("#FF4A5A");
        // health, top left
        Text(dc, "68", 60, 44, 22, cyan, bold: true);
        dc.DrawRectangle(Solid(Color.FromArgb(0x40, 0xFF, 0x4A, 0x5A)), null, new Rect(110, 54, 320, 10));
        dc.DrawRectangle(red, null, new Rect(110, 54, 230, 10));
        // minimap, top right
        double cx = w - 170, cy = 170;
        dc.DrawEllipse(Solid(Color.FromArgb(0x70, 0x08, 0x10, 0x20)), new Pen(Solid(Color.FromArgb(0xC0, 0xFF, 0x4A, 0x5A)), 2), new Point(cx, cy), 110, 110);
        dc.DrawLine(new Pen(Solid(Color.FromArgb(0x60, 0x46, 0xE6, 0xFF)), 3), new Point(cx - 80, cy + 30), new Point(cx + 70, cy - 50));
        dc.DrawLine(new Pen(Solid(Color.FromArgb(0x60, 0x46, 0xE6, 0xFF)), 3), new Point(cx - 20, cy - 90), new Point(cx + 10, cy + 90));
        dc.DrawGeometry(yellow, null, Poly((cx, cy - 12), (cx + 9, cy + 10), (cx - 9, cy + 10)));
        // ammo, bottom right
        Text(dc, "24", w - 220, h - 120, 40, yellow, bold: true);
        Text(dc, "/ 120", w - 160, h - 104, 22, cyan);
    }

    static void HudElden(DrawingContext dc, double w, double h)
    {
        void Bar(double y, double len, double fill, string color)
        {
            dc.DrawRectangle(Solid(Color.FromArgb(0x80, 0x10, 0x0C, 0x08)), new Pen(Solid(Color.FromArgb(0x90, 0xC9, 0xB0, 0x7A)), 1.5), new Rect(70, y, len, 12));
            dc.DrawRectangle(Solid(color), null, new Rect(72, y + 2, (len - 4) * fill, 8));
        }
        Bar(56, 420, 0.82, "#A8232A");
        Bar(76, 250, 0.6, "#3E5FC4");
        Bar(96, 330, 1.0, "#3C8C3A");
        // equipment cross, bottom left
        var slot = Solid(Color.FromArgb(0x70, 0x10, 0x0C, 0x08));
        var edge = new Pen(Solid(Color.FromArgb(0xA0, 0xC9, 0xB0, 0x7A)), 1.5);
        double ex = 170, ey = h - 190;
        foreach (var (dx, dy) in new[] { (0.0, -80.0), (-80.0, 0.0), (80.0, 0.0), (0.0, 80.0) })
            dc.DrawRectangle(slot, edge, new Rect(ex + dx - 34, ey + dy - 34, 68, 68));
        // runes, bottom right
        Text(dc, "48,210", w - 230, h - 110, 30, Solid("#EDE3C8"));
    }

    static void HudRdr2(DrawingContext dc, double w, double h)
    {
        double cx = 190, cy = h - 250;
        dc.DrawEllipse(Solid(Color.FromArgb(0x90, 0x1A, 0x14, 0x10)), new Pen(Solid(Color.FromArgb(0xB0, 0xE8, 0xDC, 0xC4)), 2), new Point(cx, cy), 130, 130);
        var road = new Pen(Solid(Color.FromArgb(0x70, 0xD8, 0xC8, 0xA8)), 4);
        dc.DrawLine(road, new Point(cx - 110, cy + 50), new Point(cx + 120, cy - 30));
        dc.DrawLine(road, new Point(cx + 10, cy - 125), new Point(cx - 20, cy + 125));
        dc.DrawGeometry(Solid("#F2EAD8"), null, Poly((cx, cy - 12), (cx + 9, cy + 10), (cx - 9, cy + 10)));
        // cores
        for (int i = 0; i < 3; i++)
            dc.DrawEllipse(Solid(Color.FromArgb(0xB0, 0x1A, 0x14, 0x10)), new Pen(Solid("#E8DCC4"), 2), new Point(cx - 60 + i * 60, cy + 175), 18, 18);
    }

    static void HudGow(DrawingContext dc, double w, double h)
    {
        double x = 80, y = h - 110;
        dc.DrawRectangle(Solid(Color.FromArgb(0x70, 0, 0, 0)), null, new Rect(x, y, 380, 14));
        dc.DrawRectangle(Solid("#5DBB63"), null, new Rect(x, y, 300, 14));
        dc.DrawRectangle(Solid(Color.FromArgb(0x70, 0, 0, 0)), null, new Rect(x, y + 22, 280, 8));
        dc.DrawRectangle(Solid("#D24A2C"), null, new Rect(x, y + 22, 120, 8));
        var ring = new Pen(Solid(Color.FromArgb(0xC0, 0xF4, 0xF6, 0xF8)), 2);
        for (int i = 0; i < 3; i++)
            dc.DrawEllipse(Solid(Color.FromArgb(0x60, 0x10, 0x18, 0x20)), ring, new Point(w - 280 + i * 90, h - 110), 34, 34);
    }

    static void HudCs2(DrawingContext dc, double w, double h)
    {
        var ink = Solid("#F2F2F2");
        var panel = Solid(Color.FromArgb(0x90, 0x10, 0x14, 0x18));
        // radar, top left
        dc.DrawRectangle(panel, new Pen(Solid(Color.FromArgb(0x60, 255, 255, 255)), 1), new Rect(20, 20, 280, 280));
        var lane = Solid(Color.FromArgb(0x50, 0xE6, 0xD2, 0xA8));
        dc.DrawRectangle(lane, null, new Rect(60, 60, 30, 200));
        dc.DrawRectangle(lane, null, new Rect(60, 60, 200, 30));
        dc.DrawRectangle(lane, null, new Rect(200, 60, 40, 180));
        dc.DrawEllipse(Solid("#5AB0FF"), null, new Point(215, 190), 7, 7);
        dc.DrawEllipse(Solid("#5AB0FF"), null, new Point(80, 120), 7, 7);
        // round timer and score, top center
        dc.DrawRectangle(panel, null, new Rect(w / 2 - 150, 0, 300, 56));
        Text(dc, "1:27", w / 2 - 34, 10, 28, ink, bold: true);
        Text(dc, "7", w / 2 - 120, 12, 26, Solid("#5AB0FF"), bold: true);
        Text(dc, "5", w / 2 + 100, 12, 26, Solid("#E8C15A"), bold: true);
        // health & armor, bottom left
        dc.DrawRectangle(panel, null, new Rect(20, h - 76, 320, 56));
        Text(dc, "HP 100", 36, h - 66, 30, ink, bold: true);
        Text(dc, "AR 100", 190, h - 66, 30, ink, bold: true);
        // ammo, bottom right
        dc.DrawRectangle(panel, null, new Rect(w - 260, h - 76, 240, 56));
        Text(dc, "30 / 90", w - 240, h - 66, 30, ink, bold: true);
    }

    // ═════════════════════════════ painting kit ═════════════════════════════

    static Color C(string hex) => ColorUtil.Parse(hex, "#000000");
    static Color Hex(Color c, byte a) => Color.FromArgb(a, c.R, c.G, c.B);
    static Brush Solid(string hex) => ColorUtil.Solid(C(hex));
    static Brush Solid(Color c) => ColorUtil.Solid(c);

    static Brush VGrad(params (string Hex, double At)[] stops) => Gradient(new Point(0, 1), Array.ConvertAll(stops, t => (C(t.Hex), t.At)));
    static Brush VGrad(params (Color Color, double At)[] stops) => Gradient(new Point(0, 1), stops);
    static Brush HGrad(params (string Hex, double At)[] stops) => Gradient(new Point(1, 0), Array.ConvertAll(stops, t => (C(t.Hex), t.At)));

    static Brush Gradient(Point end, (Color Color, double At)[] stops)
    {
        var b = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = end };
        foreach (var (c, at) in stops) b.GradientStops.Add(new GradientStop(c, at));
        return ColorUtil.Frozen(b);
    }

    static void Sky(DrawingContext dc, params (string Hex, double At)[] stops) =>
        dc.DrawRectangle(VGrad(stops), null, new Rect(0, 0, W, H));

    static void Ground(DrawingContext dc, double y, string top, string bottom) =>
        dc.DrawRectangle(VGrad((top, 0), (bottom, 1)), null, new Rect(0, y, W, H - y));

    static void Glow(DrawingContext dc, double x, double y, double radius, Color c, double strength)
    {
        var b = new RadialGradientBrush();
        b.GradientStops.Add(new GradientStop(Hex(c, (byte)(255 * strength)), 0));
        b.GradientStops.Add(new GradientStop(Hex(c, (byte)(90 * strength)), 0.35));
        b.GradientStops.Add(new GradientStop(Hex(c, 0), 1));
        b.Freeze();
        dc.DrawEllipse(b, null, new Point(x, y), radius, radius);
    }

    static void Fog(DrawingContext dc, double top, double bottom, Color c, double strength) =>
        dc.DrawRectangle(VGrad((Hex(c, 0), 0), (Hex(c, (byte)(255 * strength)), 0.7), (Hex(c, 0), 1)), null,
            new Rect(0, top, W, bottom - top));

    static void Vignette(DrawingContext dc, double strength)
    {
        var b = new RadialGradientBrush { RadiusX = 0.75, RadiusY = 0.75 };
        b.GradientStops.Add(new GradientStop(Color.FromArgb(0, 0, 0, 0), 0.55));
        b.GradientStops.Add(new GradientStop(Color.FromArgb((byte)(255 * strength), 0, 0, 0), 1));
        b.Freeze();
        dc.DrawRectangle(b, null, new Rect(0, 0, W, H));
    }

    static void Rays(DrawingContext dc, Random r, double x, double y, Color c, int count, double strength)
    {
        for (int i = 0; i < count; i++)
        {
            double a = Math.PI * (0.55 + r.NextDouble() * 0.5), spread = 0.03 + r.NextDouble() * 0.05, len = 1400;
            var tip = new Point(x, y);
            var p1 = new Point(x + Math.Cos(a - spread) * len, y + Math.Sin(a - spread) * len);
            var p2 = new Point(x + Math.Cos(a + spread) * len, y + Math.Sin(a + spread) * len);
            var b = new LinearGradientBrush(Hex(c, (byte)(255 * strength)), Hex(c, 0), tip, p1) { MappingMode = BrushMappingMode.Absolute };
            b.Freeze();
            dc.DrawGeometry(b, null, Poly((tip.X, tip.Y), (p1.X, p1.Y), (p2.X, p2.Y)));
        }
    }

    /// <summary>A row of towers standing on <paramref name="baseY"/>; <paramref name="broken"/> = share with ruined crowns.</summary>
    static Rect[] Skyline(DrawingContext dc, Random r, double baseY, double minH, double maxH, double minW, double maxW, Brush fill, double broken, double gap)
    {
        var rects = new List<Rect>();
        double x = -r.NextDouble() * 60;
        while (x < W)
        {
            double w = minW + r.NextDouble() * (maxW - minW);
            double h = minH + r.NextDouble() * (maxH - minH);
            var rect = new Rect(x, baseY - h, w, h + 400);
            rects.Add(rect);

            if (r.NextDouble() < broken)
            {
                double t = rect.Top, l = rect.Left, rr = rect.Right;
                dc.DrawGeometry(fill, null, Poly(
                    (l, rect.Bottom), (l, t + r.Next(0, 40)), (l + w * 0.3, t + r.Next(20, 70)),
                    (l + w * 0.45, t - r.Next(0, 30)), (l + w * 0.7, t + r.Next(30, 90)),
                    (rr, t + r.Next(0, 50)), (rr, rect.Bottom)));
            }
            else
            {
                dc.DrawRectangle(fill, null, rect);
                if (r.NextDouble() < 0.3) dc.DrawRectangle(fill, null, new Rect(x + w * 0.45, rect.Top - 40, 3, 40)); // antenna
            }
            x += w + r.NextDouble() * gap;
        }
        return rects.ToArray();
    }

    static void Windows(DrawingContext dc, Random r, IEnumerable<Rect> towers, Brush fill, double density, double cell = 18, double row = 26)
    {
        foreach (var t in towers)
            for (double y = t.Top + 30; y < Math.Min(t.Bottom, H) - 20; y += row)
                for (double x = t.Left + 10; x < t.Right - 12; x += cell)
                    if (r.NextDouble() < density) dc.DrawRectangle(fill, null, new Rect(x, y, cell * 0.5, row * 0.5));
    }

    static void Lights(DrawingContext dc, Random r, IEnumerable<Rect> towers, Color[] palette, double density)
    {
        foreach (var t in towers)
            for (double y = t.Top + 16; y < Math.Min(t.Bottom, H) - 10; y += 14)
                for (double x = t.Left + 6; x < t.Right - 8; x += 12)
                    if (r.NextDouble() < density)
                        dc.DrawRectangle(Solid(Hex(palette[r.Next(palette.Length)], (byte)r.Next(120, 230))), null, new Rect(x, y, 5, 6));
    }

    static void Neon(DrawingContext dc, Random r, IEnumerable<Rect> towers, Color[] palette)
    {
        foreach (var t in towers)
        {
            if (r.NextDouble() < 0.35) continue;
            var c = palette[r.Next(palette.Length)];
            bool vertical = r.NextDouble() < 0.5;
            double w = vertical ? 26 : t.Width * (0.5 + r.NextDouble() * 0.4);
            double h = vertical ? 120 + r.NextDouble() * 160 : 26;
            double x = t.Left + (t.Width - w) * r.NextDouble();
            double y = Math.Max(t.Top + 20, 200) + r.NextDouble() * 360;
            Glow(dc, x + w / 2, y + h / 2, Math.Max(w, h) * 0.9, c, 0.35);
            dc.DrawRoundedRectangle(Solid(Hex(c, 0x30)), new Pen(Solid(c), 3), new Rect(x, y, w, h), 4, 4);
        }
    }

    /// <summary>Ivy: a mossy crown on each roofline and strands hanging down the face.</summary>
    static void Vines(DrawingContext dc, Random r, IEnumerable<Rect> towers, Color dark, Color light, int strands = 6)
    {
        Brush Leaf() => Solid(Hex(ColorUtil.Mix(dark, light, r.NextDouble()), (byte)r.Next(190, 250)));

        foreach (var t in towers)
        {
            double face = Math.Min(t.Height, H - t.Top);

            for (double x = t.Left; x < t.Right; x += 5 + r.NextDouble() * 6) // crown
                if (r.NextDouble() < 0.7)
                {
                    double s = 3 + r.NextDouble() * 7;
                    dc.DrawEllipse(Leaf(), null, new Point(x, t.Top + r.NextDouble() * 14), s, s * 0.8);
                }

            for (int i = 0; i < strands; i++)
            {
                double x = t.Left + r.NextDouble() * t.Width, y = t.Top;
                double length = face * (0.15 + Math.Pow(r.NextDouble(), 1.5) * 0.7);
                double width = 1 + r.NextDouble();
                while (y < t.Top + length)
                {
                    double s = (2.5 + r.NextDouble() * 4.5) * width * (1 - (y - t.Top) / (length * 1.6));
                    dc.DrawEllipse(Leaf(), null, new Point(x, y), s, s * 0.85);
                    x = Math.Clamp(x + r.Next(-3, 4), t.Left, t.Right);
                    y += 3 + r.NextDouble() * 4;
                }
            }
        }
    }

    static void Canopy(DrawingContext dc, Random r, double x, double y, double size, Color dark, Color light)
    {
        for (int i = 0; i < 60; i++)
        {
            double a = r.NextDouble() * Math.PI * 2, d = r.NextDouble() * size;
            double s = 30 + r.NextDouble() * 70;
            dc.DrawEllipse(Solid(r.NextDouble() < 0.6 ? dark : light), null, new Point(x + Math.Cos(a) * d, y + Math.Sin(a) * d * 0.6), s, s * 0.8);
        }
    }

    static void Ridge(DrawingContext dc, Random r, double baseY, double amp, Brush fill, int peaks, double jag = 0.4)
    {
        var pts = new List<(double, double)> { (0, H), (0, baseY) };
        double step = W / peaks;
        for (int i = 0; i <= peaks; i++)
        {
            double x = i * step;
            pts.Add((x + step * 0.5, baseY - amp * (0.4 + r.NextDouble() * 0.6)));
            pts.Add((x + step, baseY - amp * (1 - jag) * r.NextDouble()));
        }
        pts.Add((W, H));
        dc.DrawGeometry(fill, null, Poly(pts.ToArray()));
    }

    static void Pine(DrawingContext dc, double x, double baseY, double height, Brush fill, bool snow = false)
    {
        double w = height * 0.32;
        for (int tier = 0; tier < 4; tier++)
        {
            double top = baseY - height + tier * height * 0.2;
            double tw = w * (0.45 + tier * 0.2);
            dc.DrawGeometry(fill, null, Poly((x, top), (x + tw / 2, top + height * 0.32), (x - tw / 2, top + height * 0.32)));
            if (snow)
                dc.DrawGeometry(Solid("#EEF4F8"), null, Poly((x, top), (x + tw * 0.18, top + height * 0.1), (x - tw * 0.18, top + height * 0.1)));
        }
        dc.DrawRectangle(fill, null, new Rect(x - w * 0.05, baseY - height * 0.2, w * 0.1, height * 0.2));
    }

    static void Grass(DrawingContext dc, Random r, double top, double bottom, Color light, Color dark, int blades)
    {
        for (int i = 0; i < blades; i++)
        {
            double x = r.NextDouble() * W, y = top + r.NextDouble() * (bottom - top) + 40;
            double h = 20 + r.NextDouble() * 60 * (y - top) / (bottom - top + 1);
            var pen = new Pen(Solid(ColorUtil.Mix(dark, light, r.NextDouble())), 2);
            dc.DrawLine(pen, new Point(x, y), new Point(x + r.Next(-14, 14), y - h));
        }
    }

    static void Rain(DrawingContext dc, Random r, int drops, Color c)
    {
        for (int i = 0; i < drops; i++)
        {
            double x = r.NextDouble() * (W + 200), y = r.NextDouble() * H, len = 20 + r.NextDouble() * 40;
            var pen = new Pen(Solid(Hex(c, (byte)r.Next(30, 90))), 1.2);
            dc.DrawLine(pen, new Point(x, y), new Point(x - len * 0.25, y + len));
        }
    }

    static void Snow(DrawingContext dc, Random r, int flakes)
    {
        for (int i = 0; i < flakes; i++)
        {
            double s = 1 + Math.Pow(r.NextDouble(), 3) * 5;
            dc.DrawEllipse(Solid(Color.FromArgb((byte)r.Next(120, 230), 255, 255, 255)), null, new Point(r.NextDouble() * W, r.NextDouble() * H), s, s);
        }
    }

    static void Crate(DrawingContext dc, double x, double y, double size)
    {
        var rect = new Rect(x, y, size, size);
        dc.DrawRectangle(Solid("#9A7440"), new Pen(Solid("#6E4F28"), 6), rect);
        var slat = new Pen(Solid("#6E4F28"), 5);
        dc.DrawLine(slat, rect.TopLeft, rect.BottomRight);
        dc.DrawRectangle(null, new Pen(Solid("#7E5C30"), 4), new Rect(x + 16, y + 16, size - 32, size - 32));
    }

    static void Text(DrawingContext dc, string text, double x, double y, double size, Brush brush, bool bold = false)
    {
        var ft = new FormattedText(text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
            new Typeface(new FontFamily("Bahnschrift, Segoe UI"), FontStyles.Normal, bold ? FontWeights.SemiBold : FontWeights.Normal, FontStretches.Normal),
            size, brush, 1.0);
        dc.DrawText(ft, new Point(x, y));
    }

    static Geometry Poly(params (double X, double Y)[] pts)
    {
        var g = new StreamGeometry();
        using (var c = g.Open())
        {
            c.BeginFigure(new Point(pts[0].X, pts[0].Y), true, true);
            for (int i = 1; i < pts.Length; i++) c.LineTo(new Point(pts[i].X, pts[i].Y), true, false);
        }
        g.Freeze();
        return g;
    }

    static Geometry Poly(Point[] pts)
    {
        var g = new StreamGeometry();
        using (var c = g.Open())
        {
            c.BeginFigure(pts[0], false, false);
            c.PolyLineTo(pts[1..], true, true);
        }
        g.Freeze();
        return g;
    }
}
