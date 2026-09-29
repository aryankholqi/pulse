using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Shapes;
using Pulse.Services;

namespace Pulse.Controls;

/// <summary>
/// The game summaries the player saved, newest first, as cards two to a row (one when narrow):
/// click one to open it, or delete it from the records (a second click makes sure).
/// Keeps itself current while it's on screen. Used by the settings window and the tray's saved summaries window.
/// </summary>
public sealed class SavedSummaryList : UniformGrid
{
    static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
    const string Dash = "–";
    const double Gap = 5;               // half the space between cards: each card's margin, taken back by the grid's
    const double TwoColumnsFrom = 620;  // narrower than this, one card a row

    GameSession? _confirming; // the one whose delete was clicked once: a second click deletes
    bool _listening;

    /// <summary>A saved summary was clicked.</summary>
    public event Action<GameSession>? Open;

    public SavedSummaryList()
    {
        Margin = new Thickness(-Gap);
        Columns = 2;
        VerticalAlignment = VerticalAlignment.Top; // rows as tall as their cards, not shared out over the page
        SizeChanged += (_, e) => { if (e.WidthChanged) Fit(); };

        // plain text, not bindings: rebuild when the language switches or a session is saved / deleted
        PropertyChangedEventHandler relabel = (_, _) => Build();
        Action changed = Build;
        void Listen()
        {
            if (_listening) return;
            Loc.Instance.PropertyChanged += relabel;
            SessionHistory.Changed += changed;
            _listening = true;
        }
        Loaded += (_, _) => { Listen(); Build(); };
        // its page shown again: catch up on anything saved meanwhile, even if Loaded never came
        IsVisibleChanged += (_, _) => { if (IsVisible) { Listen(); Build(); } };
        Unloaded += (_, _) =>
        {
            Loc.Instance.PropertyChanged -= relabel;
            SessionHistory.Changed -= changed;
            _listening = false;
        };
    }

    /// <summary>Two cards a row when there's room for them; a lone card or the empty note gets the whole width.</summary>
    void Fit()
    {
        int columns = Children.Count > 1 && ActualWidth >= TwoColumnsFrom ? 2 : 1;
        if (Columns != columns) Columns = columns;
    }

    void Build()
    {
        Children.Clear();
        var saved = SessionHistory.SavedSessions;
        foreach (var s in saved) Children.Add(Card(s));
        if (saved.Count == 0)
            Children.Add(new TextBlock
            {
                Text = Loc.T("SavedEmpty"),
                Foreground = (Brush)FindResource("Muted"),
                FontSize = 12.5,
                TextWrapping = TextWrapping.Wrap,
                LineHeight = 20,
                Margin = new Thickness(Gap),
            });
        Fit();
    }

    /// <summary>
    /// The game with its icon, when and how long, and the verdict; its four numbers, FPS across
    /// the session, what held it back and how hot it ran. Delete sits in the bottom corner.
    /// </summary>
    FrameworkElement Card(GameSession s)
    {
        var muted = (Brush)FindResource("Muted");
        var (verdictKey, verdict) = SessionInsights.Judge(s) switch
        {
            Smoothness.Smooth => ("SumSmooth", Palette.Cool),
            Smoothness.SomeHitches => ("SumSomeHitches", Palette.Warm),
            _ => ("SumStuttery", Palette.Hot),
        };

        // ── header: icon, game, when · how long, verdict ──
        var pill = new Border
        {
            CornerRadius = new CornerRadius(10),
            Padding = new Thickness(8, 2, 9, 3),
            BorderThickness = new Thickness(1),
            BorderBrush = verdict,
            Background = Wash(verdict, 0.1),
            VerticalAlignment = VerticalAlignment.Top,
            Margin = new Thickness(10, 0, 0, 0),
            Child = new TextBlock { Text = Loc.T(verdictKey), Foreground = verdict, FontSize = 11, FontWeight = FontWeights.SemiBold },
        };
        DockPanel.SetDock(pill, Dock.Right);

        var icon = Icon(s);
        DockPanel.SetDock(icon, Dock.Left);

        var title = new StackPanel { Margin = new Thickness(10, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
        title.Children.Add(new TextBlock { Text = s.Game, FontWeight = FontWeights.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis });
        var when = new TextBlock
        {
            Foreground = muted,
            FontSize = 11.5,
            Margin = new Thickness(0, 2, 0, 0),
            TextTrimming = TextTrimming.CharacterEllipsis,
        };
        if (s.Benchmark)
        {
            // "Benchmark · DLSS Quality · Today, 21:40 · 60 s": runs stand out from sessions, named as the player named them
            when.Inlines.Add(new System.Windows.Documents.Run(Loc.T("BenchTag")) { Foreground = (Brush)FindResource("Accent"), FontWeight = FontWeights.SemiBold });
            if (s.Label is { Length: > 0 } label) when.Inlines.Add(new System.Windows.Documents.Run("  ·  " + label) { Foreground = (Brush)FindResource("Soft") });
            when.Inlines.Add($"  ·  {SessionSummaryWindow.When(s.Ended)}  ·  {SessionSummaryWindow.RunLength(s.PlaySeconds)}");
        }
        else when.Text = $"{SessionSummaryWindow.When(s.Ended)}  ·  {SessionSummaryWindow.Duration(s.PlaySeconds)}";
        title.Children.Add(when);

        var header = new DockPanel();
        header.Children.Add(pill);
        header.Children.Add(icon);
        header.Children.Add(title);

        // ── the numbers ──
        var stats = new UniformGrid { Columns = 4, Margin = new Thickness(0, 14, 0, 0) };
        stats.Children.Add(Stat(Loc.T("SavedAvg"), Fps(s.AvgFps), verdict));
        stats.Children.Add(Stat("1% low", Fps(s.Low1), null));
        stats.Children.Add(Stat("0.1% low", Fps(s.Low01), null));
        stats.Children.Add(Stat(Loc.T("SumStutters"), s.Stutters.ToString(Inv), null));

        // ── FPS across the session ──
        var accent = ((SolidColorBrush)FindResource("Accent")).Color;
        var trend = new Sparkline
        {
            Height = 30,
            Margin = new Thickness(0, 12, 0, 0),
            Values = Array.ConvertAll(s.Timeline, v => (double)v),
            Stroke = ColorUtil.Solid(accent),
            Area = ColorUtil.Frozen(new LinearGradientBrush(Color.FromArgb(0x40, accent.R, accent.G, accent.B),
                                                            Color.FromArgb(0x00, accent.R, accent.G, accent.B), 90)),
            Baseline = (Brush)FindResource("Line"),
            FlowDirection = FlowDirection.LeftToRight, // time runs left to right in either language
        };

        // ── what held it back, how hot it ran (room left at the end for delete) ──
        string details = Details(s);
        var footer = new TextBlock
        {
            Text = details.Length > 0 ? details : " ",
            Foreground = muted,
            FontSize = 11.5,
            Margin = new Thickness(0, 10, 90, 0),
            TextTrimming = TextTrimming.CharacterEllipsis,
        };

        var content = new StackPanel { Margin = new Thickness(4, 4, 4, 2) };
        content.Children.Add(header);
        content.Children.Add(stats);
        content.Children.Add(trend);
        content.Children.Add(footer);

        var card = new Button { Style = (Style)FindResource("Card"), Content = content, Foreground = (Brush)FindResource("Text"), ToolTip = Loc.T("SavedOpen") };
        card.Click += (_, _) => Open?.Invoke(s);

        // delete: laid over the card's bottom corner, not inside it, so clicking it never opens the summary
        bool confirming = _confirming is { } c && c.Started == s.Started && c.Process == s.Process;
        var delete = new Button
        {
            Style = (Style)FindResource("Link"),
            Content = Loc.T(confirming ? "SavedDeleteSure" : "SavedDelete"),
            Foreground = confirming ? Palette.Hot : muted,
            FontSize = 12,
            Margin = new Thickness(0, 0, 16, 11),
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Bottom,
            ToolTip = Loc.T("SavedDeleteHint"),
        };
        delete.Click += (_, _) =>
        {
            if (confirming)
            {
                _confirming = null;
                SessionHistory.Delete(s); // raises Changed → Build
            }
            else
            {
                _confirming = s;
                Build();
            }
        };

        var cell = new Grid { Margin = new Thickness(Gap) };
        cell.Children.Add(card);
        cell.Children.Add(delete);
        return cell;
    }

    /// <summary>A number with its label under it.</summary>
    FrameworkElement Stat(string label, string value, Brush? dot)
    {
        var line = new StackPanel { Orientation = Orientation.Horizontal, FlowDirection = FlowDirection.LeftToRight, HorizontalAlignment = HorizontalAlignment.Left };
        if (dot != null)
            line.Children.Add(new Ellipse { Width = 7, Height = 7, Fill = dot, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 2, 7, 0) });
        line.Children.Add(new TextBlock
        {
            Text = value,
            FontSize = 18,
            FontWeight = FontWeights.SemiBold,
            FontFamily = new FontFamily("Bahnschrift, Segoe UI"),
        });

        var stack = new StackPanel { Margin = new Thickness(0, 0, 8, 0) };
        stack.Children.Add(line);
        stack.Children.Add(new TextBlock
        {
            Text = label,
            Foreground = (Brush)FindResource("Muted"),
            FontSize = 11,
            Margin = new Thickness(0, 1, 0, 0),
            TextTrimming = TextTrimming.CharacterEllipsis,
        });
        return stack;
    }

    /// <summary>The icon read from the game's .exe, or its first letter on a tile.</summary>
    FrameworkElement Icon(GameSession s)
    {
        var tile = new Border { Width = 36, Height = 36, CornerRadius = new CornerRadius(9), Background = (Brush)FindResource("Raised") };
        if (GameIcons.For(s.ExePath ?? GameIcons.FindExe(s.Process, lookAtRunning: false)) is { } image)
        {
            var img = new Image { Source = image, Width = 26, Height = 26, Stretch = Stretch.Uniform };
            RenderOptions.SetBitmapScalingMode(img, BitmapScalingMode.HighQuality);
            tile.Child = img;
        }
        else
            tile.Child = new TextBlock
            {
                Text = s.Game.Length > 0 ? s.Game[..1].ToUpperInvariant() : "?",
                FontSize = 16,
                FontWeight = FontWeights.SemiBold,
                FontFamily = new FontFamily("Bahnschrift, Segoe UI"),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
            };
        return tile;
    }

    /// <summary>"GPU-bound 78%  ·  GPU 72°  ·  CPU 65°": whichever of these the session knows.</summary>
    static string Details(GameSession s)
    {
        var parts = new List<string>();
        if (s.GpuBound is double gpu && s.CpuBound is double cpu && s.Capped is double capped)
        {
            var (name, share) = gpu >= cpu && gpu >= capped ? ("GPU-bound", gpu)
                              : cpu >= capped ? ("CPU-bound", cpu)
                              : ("Capped", capped);
            parts.Add($"{name} {Math.Round(share * 100).ToString("0", Inv)}%");
        }
        if (s.GpuTempMax is float gt) parts.Add($"GPU {gt.ToString("0", Inv)}°");
        if (s.CpuTempMax is float ct) parts.Add($"CPU {ct.ToString("0", Inv)}°");
        return string.Join("  ·  ", parts);
    }

    static string Fps(double? fps) => fps is double f ? Math.Round(f).ToString("0", Inv) : Dash;

    static Brush Wash(Brush brush, double opacity)
    {
        var b = brush.CloneCurrentValue();
        b.Opacity = opacity;
        b.Freeze();
        return b;
    }
}
