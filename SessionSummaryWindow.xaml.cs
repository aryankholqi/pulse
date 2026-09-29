using System;
using System.ComponentModel;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Shapes;
using Pulse.Controls;
using Pulse.Services;

namespace Pulse;

/// <summary>Shown when a game closes (or from the tray): how the session went and what to try next.</summary>
public partial class SessionSummaryWindow : Window
{
    static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
    const string Dash = "–";

    readonly GameSession _session;
    readonly GameSession? _previous;
    readonly AppSettings _settings;
    readonly Brush _gpuHue, _cpuHue;

    /// <summary>The user flipped "show after every game" here: the app saves it.</summary>
    public event Action? SettingChanged;

    internal SessionSummaryWindow(GameSession session, GameSession? previous, AppSettings settings)
    {
        _session = session;
        _previous = previous;
        _settings = settings;
        _gpuHue = ColorUtil.Solid(ColorUtil.Parse(settings.GpuColor, AppSettings.DefaultGpuColor));
        _cpuHue = ColorUtil.Solid(ColorUtil.Parse(settings.CpuColor, AppSettings.DefaultCpuColor));

        InitializeComponent();

        Timeline.Values = session.Timeline;
        Timeline.SecondsPerPoint = session.TimelineSeconds;
        Timeline.Average = session.AvgFps;
        Timeline.Stroke = ColorUtil.Solid(ColorUtil.Parse(settings.FpsColor, AppSettings.DefaultFpsColor));

        ShowAfterGames.IsChecked = settings.ShowSessionSummary;
        ShowAfterGames.Checked += (_, _) => { _settings.ShowSessionSummary = true; SettingChanged?.Invoke(); };
        ShowAfterGames.Unchecked += (_, _) => { _settings.ShowSessionSummary = false; SettingChanged?.Invoke(); };

        GuideLink.Click += (_, _) => new BottleneckGuideWindow(_settings) { Owner = this }.ShowDialog();
        OkButton.Click += (_, _) => Close();
        SaveButton.Click += (_, _) => SessionHistory.SetSaved(_session, !SessionHistory.IsSaved(_session));

        Build();

        // plain text, not bindings: rebuild when the language switches
        PropertyChangedEventHandler relabel = (_, _) => Build();
        Loc.Instance.PropertyChanged += relabel;
        Action saved = UpdateSaveButton; // saved or deleted here or in the saved summaries window
        SessionHistory.Changed += saved;
        Closed += (_, _) =>
        {
            Loc.Instance.PropertyChanged -= relabel;
            SessionHistory.Changed -= saved;
        };

        SourceInitialized += (_, _) =>
        {
            Native.UseDarkTitleBar(new WindowInteropHelper(this).Handle);
            // a compact window by default, the middle scrolls; never taller than the screen (a small laptop or a high scale)
            MaxHeight = SystemParameters.WorkArea.Height;
            Height = Math.Min(Height, SystemParameters.WorkArea.Height * 0.9);
        };
        SizeChanged += (_, e) => { if (e.WidthChanged) Relayout(e.NewSize.Width); };
    }

    /// <summary>Narrow window: the numbers two by two, the GPU above the CPU, the switch under the buttons.</summary>
    void Relayout(double width)
    {
        Tiles.Columns = width < 600 ? 2 : 4;
        HardwareGrid.Columns = width < 520 ? 1 : 2;

        bool narrow = width < 560;
        Grid.SetRow(ShowAfterGames, narrow ? 1 : 0);
        Grid.SetColumnSpan(ShowAfterGames, narrow ? 2 : 1);
        ShowAfterGames.Margin = new Thickness(0, narrow ? 14 : 0, 0, 0);
    }

    void Build()
    {
        var s = _session;

        // ── header ──
        GameTitle.Text = s.Game;
        GameTitle.ToolTip = s.Process + ".exe";
        Subtitle.Text = $"{Duration(s.PlaySeconds)}  ·  {When(s.Ended)}";

        var (verdictKey, verdictBrush) = SessionInsights.Judge(s) switch
        {
            Smoothness.Smooth => ("SumSmooth", Palette.Cool),
            Smoothness.SomeHitches => ("SumSomeHitches", Palette.Warm),
            _ => ("SumStuttery", Palette.Hot),
        };
        VerdictText.Text = Loc.T(verdictKey);
        VerdictText.Foreground = verdictBrush;
        VerdictDot.Fill = verdictBrush;
        VerdictPill.BorderBrush = verdictBrush;
        VerdictPill.Background = Wash(verdictBrush, 0.1);

        // ── numbers ──
        var p = _previous;
        Comparison.Text = p is null
            ? Loc.T("SumFirst")
            : string.Format(Loc.T("SumVsLast"), $"{When(p.Ended)}, {Duration(p.PlaySeconds)}");

        Tiles.Children.Clear();
        Tiles.Children.Add(Tile(Loc.T("SumAvg"), Fps(s.AvgFps), null, Delta(s.AvgFps, p?.AvgFps, higherIsBetter: true)));
        Tiles.Children.Add(Tile("1% low", Fps(s.Low1), null, Delta(s.Low1, p?.Low1, higherIsBetter: true)));
        Tiles.Children.Add(Tile("0.1% low", Fps(s.Low01), null, Delta(s.Low01, p?.Low01, higherIsBetter: true)));
        double perMinute = SessionInsights.StuttersPerMinute(s);
        Tiles.Children.Add(Tile(Loc.T("SumStutters"), s.Stutters.ToString(Inv),
            string.Format(Loc.T("SumPerMinute"), perMinute.ToString("0.0", Inv)),
            p is null ? null : Delta(perMinute, SessionInsights.StuttersPerMinute(p), higherIsBetter: false, rate: true)));

        // ── what limited it ──
        if (s.GpuBound is double gpu && s.CpuBound is double cpu && s.Capped is double capped)
        {
            BoundBox.Visibility = Visibility.Visible;
            var soft = (Brush)FindResource("Soft");
            BoundBar.Parts = new[]
            {
                new SplitBar.Part(gpu, _gpuHue, false, $"GPU-bound  {Pct(gpu)}"),
                new SplitBar.Part(cpu, _cpuHue, false, $"CPU-bound  {Pct(cpu)}"),
                new SplitBar.Part(capped, soft, true, $"Capped  {Pct(capped)}"),
            };
            BoundLegend.Children.Clear();
            LegendItem("GPU-bound", gpu, _gpuHue);
            LegendItem("CPU-bound", cpu, _cpuHue);
            LegendItem("Capped", capped, soft);
        }
        else BoundBox.Visibility = Visibility.Collapsed;

        // ── hardware ──
        HardwareGrid.Children.Clear();
        bool haveGpu = s.GpuTempMax is not null || s.GpuLoadAvg is not null;
        bool haveCpu = s.CpuTempMax is not null || s.CpuLoadAvg is not null;
        if (haveGpu)
        {
            var col = Column("GPU", OverlayViewModel.ShortGpu(s.GpuName));
            Row(col, Loc.T("SumTemp"), TempRange(s.GpuTempAvg, s.GpuTempMax), Palette.ForTemp(s.GpuTempMax));
            Row(col, Loc.T("SumLoad"), s.GpuLoadAvg is float gl ? gl.ToString("0", Inv) + "%" : Dash, null);
            if (s.VramPeakMb is float vp)
            {
                string vram = (vp / 1024).ToString("0.0", Inv)
                              + (s.VramTotalMb is float vt ? " / " + (vt / 1024).ToString("0", Inv) : "") + " GB";
                bool full = s.VramTotalMb is float t && t > 0 && vp / t >= 0.95;
                Row(col, Loc.T("SumVram"), vram, full ? Palette.Warm : null);
            }
            HardwareGrid.Children.Add(col);
        }
        if (haveCpu)
        {
            var col = Column("CPU", OverlayViewModel.ShortCpu(s.CpuName));
            Row(col, Loc.T("SumTemp"), TempRange(s.CpuTempAvg, s.CpuTempMax), Palette.ForTemp(s.CpuTempMax));
            Row(col, Loc.T("SumLoad"), s.CpuLoadAvg is float cl ? cl.ToString("0", Inv) + "%" : Dash, null);
            HardwareGrid.Children.Add(col);
        }
        HardwareGrid.Visibility = haveGpu || haveCpu ? Visibility.Visible : Visibility.Collapsed;
        NoSensors.Visibility = haveGpu || haveCpu ? Visibility.Collapsed : Visibility.Visible;

        BuildRam(s);
        UpdateSaveButton();

        // ── tips ──
        TipList.Children.Clear();
        var accent = (Brush)FindResource("Accent");
        var textSoft = (Brush)FindResource("Soft");
        foreach (var tip in SessionInsights.Tips(s))
        {
            var dot = new Ellipse { Width = 5, Height = 5, Fill = accent, Margin = new Thickness(0, 8, 10, 0), VerticalAlignment = VerticalAlignment.Top };
            DockPanel.SetDock(dot, Dock.Left);
            var row = new DockPanel { Margin = new Thickness(0, 6, 0, 0) };
            row.Children.Add(dot);
            row.Children.Add(new TextBlock { Text = tip, Foreground = textSoft, TextWrapping = TextWrapping.Wrap, LineHeight = 20 });
            TipList.Children.Add(row);
        }
    }

    /// <summary>"Save" or "✓ Saved" (a click on that one unsaves).</summary>
    void UpdateSaveButton()
    {
        bool saved = SessionHistory.IsSaved(_session);
        SaveButton.Content = Loc.T(saved ? "SumSaved" : "SumSave");
        SaveButton.Foreground = saved ? Palette.Cool : (Brush)FindResource("Soft");
        SaveButton.ToolTip = saved ? Loc.T("SumSavedHint") : Loc.T("SumSaveHint");
    }

    /// <summary>RAM use during the game, the sticks, and what an upgrade would likely have added.</summary>
    void BuildRam(GameSession s)
    {
        var m = s.RamModules;
        RamRows.Children.Clear();
        RamUpgrades.Children.Clear();
        RamBox.Visibility = m is null && s.RamPeakGb is null ? Visibility.Collapsed : Visibility.Visible;
        RamKit.Text = m is null ? "" : RamAdvice.Describe(m);

        if (s.RamPeakGb is float peak)
        {
            float total = s.RamTotalGb ?? m?.TotalGb ?? 0;
            string used = string.Format(Loc.T("SumRamUsedValue"),
                (s.RamAvgGb ?? peak).ToString("0.0", Inv), peak.ToString("0.0", Inv), total.ToString("0", Inv));
            bool full = total > 0 && peak / total >= 0.9;
            Row(RamRows, Loc.T("SumRamUsed"), used, full ? Palette.Warm : null);
        }
        if (m is not null)
        {
            string channels = m.Channels switch
            {
                1 => Loc.T("SumRamSingle"),
                2 => Loc.T("SumRamDual"),
                int n => string.Format(Loc.T("SumRamMulti"), n),
            };
            Row(RamRows, Loc.T("SumRamChannels"), channels, m.Channels == 1 ? Palette.Warm : null);
        }

        var options = RamAdvice.Options(s);
        RamUpgradeBox.Visibility = m is null ? Visibility.Collapsed : Visibility.Visible;
        foreach (var o in options)
            Row(RamUpgrades, o.Label, RamAdvice.Format(o) + " FPS", o.High >= 0.01 ? Palette.Cool : (Brush)FindResource("Muted"));
        RamBasis.Text = s.CpuBound is not double cpu ? Loc.T("SumRamNoBound")
            : options.Count == 0 ? Loc.T("SumRamFine")
            : string.Format(Loc.T("SumRamBasis"), Pct(cpu));
    }

    // ───────────────────────── pieces ─────────────────────────

    /// <summary>One number: label, value, an optional small line under it, and its change since last time.</summary>
    Border Tile(string label, string value, string? note, (string Text, Brush Brush)? delta)
    {
        var stack = new StackPanel();
        stack.Children.Add(new TextBlock { Text = label, Foreground = (Brush)FindResource("Muted"), FontSize = 11.5, FontWeight = FontWeights.SemiBold });
        stack.Children.Add(new TextBlock
        {
            Text = value,
            FontSize = 26,
            FontWeight = FontWeights.SemiBold,
            FontFamily = new FontFamily("Bahnschrift, Segoe UI"),
            Margin = new Thickness(0, 2, 0, 0),
            FlowDirection = FlowDirection.LeftToRight,
            HorizontalAlignment = HorizontalAlignment.Left, // mirrored in Persian by the parent
        });
        if (note != null)
            stack.Children.Add(new TextBlock { Text = note, Foreground = (Brush)FindResource("Muted"), FontSize = 11.5 });
        if (delta is { } d)
            stack.Children.Add(new TextBlock { Text = d.Text, Foreground = d.Brush, FontSize = 11.5, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 3, 0, 0) });

        return new Border
        {
            Background = (Brush)FindResource("Surface"),
            BorderBrush = (Brush)FindResource("Line"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(12),
            Padding = new Thickness(14, 10, 12, 12),
            Margin = new Thickness(4), // the grid's -4 margin evens it out, two rows or one
            Child = stack,
        };
    }

    /// <summary>▲ / ▼ with the change, green when it got better; "same" inside ±3%.</summary>
    (string, Brush)? Delta(double? now, double? before, bool higherIsBetter, bool rate = false)
    {
        if (now is not double a || before is not double b) return null;

        double change = a - b;
        bool same = rate ? Math.Abs(change) < 0.2 : b > 0 && Math.Abs(change / b) < 0.03;
        if (same) return ("= " + Loc.T("SumSame"), (Brush)FindResource("Muted"));

        bool better = (change > 0) == higherIsBetter;
        string amount = rate ? Math.Abs(change).ToString("0.0", Inv) : Math.Abs(Math.Round(change)).ToString("0", Inv);
        return ($"{(change > 0 ? "▲" : "▼")} {amount}", better ? Palette.Cool : Palette.Hot);
    }

    void LegendItem(string name, double share, Brush hue)
    {
        var item = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 18, 0) };
        item.Children.Add(new Ellipse { Width = 8, Height = 8, Fill = hue, VerticalAlignment = VerticalAlignment.Center });
        item.Children.Add(new TextBlock { Text = name, Margin = new Thickness(7, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center });
        item.Children.Add(new TextBlock
        {
            Text = Pct(share),
            Foreground = (Brush)FindResource("Muted"),
            Margin = new Thickness(6, 0, 0, 0),
            VerticalAlignment = VerticalAlignment.Center,
            FlowDirection = FlowDirection.LeftToRight,
        });
        BoundLegend.Children.Add(item);
    }

    StackPanel Column(string title, string name)
    {
        var col = new StackPanel { Margin = new Thickness(0, 0, 16, 6) }; // the bottom: when stacked in a narrow window
        var head = new TextBlock { Margin = new Thickness(0, 0, 0, 4), TextTrimming = TextTrimming.CharacterEllipsis };
        head.Inlines.Add(new System.Windows.Documents.Run(title) { FontWeight = FontWeights.SemiBold });
        if (name.Length > 0)
            head.Inlines.Add(new System.Windows.Documents.Run("  " + name) { Foreground = (Brush)FindResource("Muted"), FontSize = 11.5 });
        col.Children.Add(head);
        return col;
    }

    void Row(StackPanel col, string label, string value, Brush? valueBrush)
    {
        var row = new DockPanel { Margin = new Thickness(0, 3, 0, 0) };
        var v = new TextBlock
        {
            Text = value,
            Foreground = valueBrush ?? (Brush)FindResource("Text"),
            FlowDirection = FlowDirection.LeftToRight,
        };
        DockPanel.SetDock(v, Dock.Right);
        row.Children.Add(v);
        row.Children.Add(new TextBlock { Text = label, Foreground = (Brush)FindResource("Soft") });
        col.Children.Add(row);
    }

    // ───────────────────────── formatting ─────────────────────────

    static string Fps(double? fps) => fps is double f ? Math.Round(f).ToString("0", Inv) : Dash;

    static string Pct(double share) => Math.Round(share * 100).ToString("0", Inv) + "%";

    string TempRange(float? avg, float? max) =>
        max is float m
            ? string.Format(Loc.T("SumAvgPeak"), avg is float a ? a.ToString("0", Inv) + "°" : Dash, m.ToString("0", Inv) + "°")
            : Dash;

    internal static string Duration(double seconds)
    {
        var t = TimeSpan.FromSeconds(Math.Max(60, seconds));
        return t.TotalHours >= 1
            ? string.Format(Loc.T("SumHours"), (int)t.TotalHours, t.Minutes)
            : string.Format(Loc.T("SumMinutes"), (int)Math.Round(t.TotalMinutes));
    }

    /// <summary>"Today, 21:40" · "Yesterday, 21:40" · a date (Persian calendar in Persian).</summary>
    internal static string When(DateTime at)
    {
        string time = at.ToString("HH:mm", Inv);
        var today = DateTime.Today;
        if (at.Date == today) return string.Format(Loc.T("SumToday"), time);
        if (at.Date == today.AddDays(-1)) return string.Format(Loc.T("SumYesterday"), time);

        if (Loc.Instance.Language == AppLanguage.Persian)
        {
            var pc = new PersianCalendar();
            return $"{pc.GetYear(at)}/{pc.GetMonth(at):00}/{pc.GetDayOfMonth(at):00}، {time}";
        }
        return at.ToString("MMM d", Inv) + ", " + time;
    }

    static Brush Wash(Brush brush, double opacity)
    {
        var b = brush.CloneCurrentValue();
        b.Opacity = opacity;
        b.Freeze();
        return b;
    }
}
