using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using System.Windows.Threading;
using Pulse.Services;

namespace Pulse;

/// <summary>The Boost page: the while-you-play switches, the NVIDIA power limit, and the throttle check. Live while shown.</summary>
public partial class BoostPage : UserControl
{
    static readonly Brush Good = Frozen(0x8F, 0xE3, 0xB0), Warn = Frozen(0xFF, 0xD1, 0x66), Bad = Frozen(0xFF, 0x9A, 0x85);
    static readonly int[] Presets = { 70, 80, 90, 100 };

    AppSettings _settings = null!;
    GameBoost _boost = null!;
    Action _changed = null!;
    readonly DispatcherTimer _timer, _applyPower, _applyTune;
    bool _powerReady, _powerRefused;
    bool _tuneReady, _tuneRefused, _syncing;

    public BoostPage()
    {
        InitializeComponent();
        _timer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromSeconds(1) };
        _timer.Tick += (_, _) => Refresh();
        // a slider drag is one driver call, once it settles
        _applyPower = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(400) };
        _applyPower.Tick += (_, _) => { _applyPower.Stop(); ApplyPower(); };
        // clocks: once the slider rests, so a drag isn't a dozen trials
        _applyTune = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(600) };
        _applyTune.Tick += (_, _) => { _applyTune.Stop(); ApplyTune(); };
        IsVisibleChanged += (_, _) =>
        {
            if (IsVisible) { Refresh(); _timer.Start(); }
            else _timer.Stop();
        };

        // the live dot breathes: this panel is a reading, not a setting
        var breathe = new DoubleAnimation(0.15, 0.45, TimeSpan.FromSeconds(1.2)) { AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever };
        ThrottleHalo.BeginAnimation(OpacityProperty, breathe);
    }

    internal void Attach(AppSettings settings, GameBoost boost, Action changed)
    {
        _settings = settings;
        _boost = boost;
        _changed = changed;

        Wire(PlanSwitch, () => settings.BoostPowerPlan, v => settings.BoostPowerPlan = v);
        Wire(PrioritySwitch, () => settings.BoostPriority, v => settings.BoostPriority = v);
        Wire(BackgroundSwitch, () => settings.BoostBackground, v => settings.BoostBackground = v);
        if (GameBoost.VCache is null) VCacheSwitch.IsEnabled = false; // shown off, the choice kept
        else Wire(VCacheSwitch, () => settings.BoostVCache, v => settings.BoostVCache = v);

        foreach (int pct in Presets)
        {
            var b = new RadioButton { Style = (Style)FindResource("Segment"), GroupName = "power", Padding = new Thickness(0, 6, 0, 6), Tag = pct };
            b.Checked += (_, _) => { if (PowerSlider.Value != pct) PowerSlider.Value = pct; };
            PowerPresets.Children.Add(b);
        }
        PowerSlider.ValueChanged += (_, _) =>
        {
            if (!_powerReady) return;
            _powerRefused = false;
            ShowPower();
            _applyPower.Stop();
            _applyPower.Start();
        };
        PowerReset.Click += (_, _) => PowerSlider.Value = 100;

        TuneUnlock.IsChecked = settings.GpuTuningUnlocked;
        RoutedEventHandler unlock = (_, _) =>
        {
            settings.GpuTuningUnlocked = TuneUnlock.IsChecked == true;
            if (!settings.GpuTuningUnlocked && _boost.TuningApplied) _boost.ResetTuning(); // locked = stock
            _changed();
            Refresh();
        };
        TuneUnlock.Checked += unlock;
        TuneUnlock.Unchecked += unlock;
        CapSlider.ValueChanged += (_, _) => TuneMoved();
        OffsetSlider.ValueChanged += (_, _) => TuneMoved();
        UseGameClock.Click += (_, _) =>
        {
            if (_boost.GameClock is { } c) CapSlider.Value = Math.Min(CapSlider.Maximum, Math.Round(c / 15) * 15);
        };
        TuneKeep.Click += (_, _) =>
        {
            settings.GpuTuningTested = true;
            _changed();
            Refresh();
        };
        TuneUndo.Click += (_, _) =>
        {
            _applyTune.Stop();
            _boost.ResetTuning();
            _tuneRefused = false;
            _changed();
            Refresh();
        };
    }

    void Wire(CheckBox box, Func<bool> get, Action<bool> set)
    {
        box.IsChecked = get();
        RoutedEventHandler flip = (_, _) =>
        {
            set(box.IsChecked == true);
            _changed();
            _boost.Apply(); // mid-game: now, not on the next tick
            Refresh();
        };
        box.Checked += flip;
        box.Unchecked += flip;
    }

    void Refresh()
    {
        if (_boost is null) return;
        RefreshStatus();
        RefreshPower();
        RefreshThrottle();
        RefreshTuning();

        VCacheHint.Text = GameBoost.VCache is { } v
            ? string.Format(Loc.T("BoostVCacheHint"), v.Threads, v.CacheMb, v.OtherCacheMb)
            : Loc.T("BoostVCacheNone");
        VCacheSwitch.ToolTip = GameBoost.VCache is null ? Loc.T("BoostVCacheNone") : null;
    }

    // ───────────────────────── status ─────────────────────────

    void RefreshStatus()
    {
        StatusChips.Children.Clear();
        bool anyOn = _settings.BoostPowerPlan || _settings.BoostPriority || _settings.BoostBackground
                     || (_settings.BoostVCache && GameBoost.VCache != null);

        if (_boost.Game is { } game)
        {
            SetBadge(Good, Loc.T("BoostBadgeOn"));
            StatusTitle.Text = game + ".exe";
            StatusText.Text = string.Format(Loc.T("BoostActiveSince"), _boost.Since.ToString("HH:mm"),
                (int)(DateTime.Now - _boost.Since).TotalMinutes);
            StatusTile.Background = Wash(Good, 0x26);
            StatusGlyph.Foreground = Good;

            foreach (var t in _boost.Active) StatusChips.Children.Add(Chip(TweakName(t), Good));
            foreach (var t in _boost.Blocked) StatusChips.Children.Add(Chip(string.Format(Loc.T("BoostBlocked"), TweakName(t)), Warn));
            if (StatusChips.Children.Count == 0) StatusChips.Children.Add(Chip(Loc.T("BoostNothingOn"), (Brush)FindResource("Muted")));
        }
        else
        {
            var muted = (Brush)FindResource("Muted");
            SetBadge(anyOn ? (Brush)FindResource("Accent") : muted, Loc.T(anyOn ? "BoostBadgeReady" : "BoostBadgeOff"));
            StatusTitle.Text = Loc.T(anyOn ? "BoostWaiting" : "BoostOff");
            StatusText.Text = Loc.T(anyOn ? "BoostWaitingHint" : "BoostOffHint");
            StatusTile.Background = (Brush)FindResource("AccentWash");
            StatusGlyph.Foreground = anyOn ? (Brush)FindResource("Accent") : muted;
        }
    }

    string TweakName(GameBoost.Tweak t) => t switch
    {
        GameBoost.Tweak.PowerPlan => Loc.T("BoostChipPlan"),
        GameBoost.Tweak.Priority => Loc.T("BoostChipPriority"),
        GameBoost.Tweak.Background => string.Format(Loc.T("BoostChipBackground"), _boost.CalmedApps),
        _ => Loc.T("BoostChipVCache"),
    };

    void SetBadge(Brush color, string text)
    {
        StatusBadge.Background = Wash(color, 0x22);
        StatusDot.Fill = color;
        StatusBadgeText.Foreground = color;
        StatusBadgeText.Text = text;
    }

    static Border Chip(string text, Brush color) => new()
    {
        CornerRadius = new CornerRadius(9),
        Background = Wash(color, 0x1A),
        BorderBrush = Wash(color, 0x40),
        BorderThickness = new Thickness(1),
        Padding = new Thickness(9, 3, 10, 4),
        Margin = new Thickness(0, 0, 6, 6),
        Child = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Children =
            {
                new Ellipse { Width = 6, Height = 6, Fill = color, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 7, 0) },
                new TextBlock { Text = text, FontSize = 11.5, Foreground = color },
            },
        },
    };

    // ───────────────────────── power limit ─────────────────────────

    void RefreshPower()
    {
        var limits = _boost.Gpu?.PowerLimits;
        string? unavailable = _boost.GpuPending ? Loc.T("GpuLooking")
            : _boost.Gpu is null ? Loc.T("PowerNoNvidia")
            : limits is null ? Loc.T("PowerLocked") : null;
        PowerBody.Visibility = unavailable is null ? Visibility.Visible : Visibility.Collapsed;
        PowerReset.Visibility = PowerBody.Visibility;
        PowerUnavailable.Visibility = unavailable is null ? Visibility.Collapsed : Visibility.Visible;
        PowerUnavailable.Text = unavailable ?? "";
        if (limits is not { } l) return;

        if (!_powerReady)
        {
            PowerSlider.Minimum = Math.Ceiling(l.Min / l.Stock * 100);
            PowerSlider.Maximum = Math.Floor(l.Max / l.Stock * 100);
            PowerSlider.Value = _settings.GpuPowerLimit ?? Math.Round(l.Now / l.Stock * 100);
            PowerMin.Text = $"{PowerSlider.Minimum:0}%";
            PowerMax.Text = $"{PowerSlider.Maximum:0}%";
            foreach (RadioButton b in PowerPresets.Children)
                b.IsEnabled = (int)b.Tag >= PowerSlider.Minimum && (int)b.Tag <= PowerSlider.Maximum;
            _powerReady = true;
        }
        ShowPower();

        float limit = l.Stock * (float)PowerSlider.Value / 100;
        if (_boost.Gpu?.PowerWatts is { } draw)
        {
            PowerDraw.Text = $"{draw:0} W";
            PowerMeter.Value = draw / limit * 100;
            PowerMeter.Fill = draw >= limit * 0.95f ? Warn : (Brush)FindResource("Accent");
        }
        else PowerDraw.Text = "–";
    }

    /// <summary>The number, its watts, its preset, and what it means: from the slider, before the driver hears of it.</summary>
    void ShowPower()
    {
        if (_boost.Gpu?.PowerLimits is not { } l) return;
        int pct = (int)PowerSlider.Value;
        PowerPercent.Text = $"{pct}%";
        PowerWatts.Text = string.Format(Loc.T("PowerWatts"), l.Stock * pct / 100, l.Stock);

        (string tag, Brush color, string note) = pct switch
        {
            < 70 => (Loc.T("PowerTagLow"), Warn, Loc.T("PowerNoteLow")),
            < 90 => (Loc.T("PowerTagSweet"), Good, Loc.T("PowerNoteSweet")),
            < 100 => (Loc.T("PowerTagCool"), Good, Loc.T("PowerNoteCool")),
            100 => (Loc.T("PowerTagStock"), (Brush)FindResource("Muted"), Loc.T("PowerNoteStock")),
            _ => (Loc.T("PowerTagHigh"), Warn, Loc.T("PowerNoteHigh")),
        };
        PowerTag.Text = tag;
        PowerTag.Foreground = color;
        PowerNote.Text = _powerRefused ? Loc.T("PowerRefused") : note;
        PowerNote.Foreground = _powerRefused ? Bad : (Brush)FindResource("Muted");
        PowerNoteGlyph.Foreground = PowerNote.Foreground;

        foreach (RadioButton b in PowerPresets.Children)
        {
            int p = (int)b.Tag;
            b.Content = $"{p}%";
            if (b.IsChecked != (p == pct)) b.IsChecked = p == pct;
        }
    }

    void ApplyPower()
    {
        int pct = (int)PowerSlider.Value;
        _powerRefused = !_boost.SetPowerLimit(pct);
        if (!_powerRefused)
        {
            _settings.GpuPowerLimit = pct == 100 ? null : pct;
            _changed();
        }
        ShowPower();
    }

    // ───────────────────────── throttle check ─────────────────────────

    void RefreshThrottle()
    {
        var reasons = _boost.ReadReasons();
        string? unavailable = _boost.GpuPending ? Loc.T("GpuLooking")
            : reasons is null ? Loc.T("ThrottleNoNvidia") : null;
        ThrottleBody.Visibility = unavailable is null ? Visibility.Visible : Visibility.Collapsed;
        ThrottleUnavailable.Visibility = unavailable is null ? Visibility.Collapsed : Visibility.Visible;
        ThrottleUnavailable.Text = unavailable ?? "";
        if (reasons is not { } r) return;

        // the worst reason wins: heat and hardware before power, power before idle
        (string key, Brush color) = (r & GameBoost.ReasonThermal) != 0 ? ("Heat", Bad)
            : (r & GameBoost.ReasonHardware) != 0 ? ("Hardware", Bad)
            : (r & GameBoost.ReasonPower) != 0 ? ("Power", Warn)
            : (r & GameBoost.ReasonIdle) != 0 ? ("Idle", (Brush)FindResource("Muted"))
            : ("Full", Good);
        ThrottleState.Text = Loc.T("ThrottleState" + key);
        ThrottleState.Foreground = color;
        ThrottleWhy.Text = Loc.T("ThrottleWhy" + key);
        ThrottleDot.Fill = ThrottleHalo.Fill = color;
        ThrottleCard.Background = Wash(color, 0x12);
        ThrottleCard.BorderBrush = Wash(color, 0x33);

        var power = _boost.ThrottleShare(GameBoost.ReasonPower);
        ThrottleBars.Visibility = power is null ? Visibility.Collapsed : Visibility.Visible;
        ThrottleIdle.Visibility = power is null ? Visibility.Visible : Visibility.Collapsed;
        if (power is null) return;
        Bar(BarPower, ValPower, power, Warn);
        Bar(BarHeat, ValHeat, _boost.ThrottleShare(GameBoost.ReasonThermal), Bad);
        Bar(BarHardware, ValHardware, _boost.ThrottleShare(GameBoost.ReasonHardware), Bad);
    }

    void Bar(Controls.MeterBar bar, TextBlock value, double? share, Brush color)
    {
        double pct = (share ?? 0) * 100;
        bar.Value = pct;
        bar.Fill = pct > 0 ? color : (Brush)FindResource("Faint");
        value.Text = $"{pct:0}%";
    }

    // ───────────────────────── undervolt ─────────────────────────

    bool CapIsOff => CapSlider.Value >= CapSlider.Maximum;

    void TuneMoved()
    {
        if (!_tuneReady || _syncing) return;
        if (CapIsOff && OffsetSlider.Value != 0)
        {
            _syncing = true;
            OffsetSlider.Value = 0; // no cap: the offset would overclock
            _syncing = false;
        }
        _settings.GpuClockCap = CapIsOff ? null : (int)CapSlider.Value;
        _settings.GpuClockOffset = OffsetRow.Visibility == Visibility.Visible ? (int)OffsetSlider.Value : 0;
        _settings.GpuTuningTested = false; // new numbers: a new trial
        _tuneRefused = false;
        ShowTuneValues();
        _applyTune.Stop();
        _applyTune.Start();
    }

    void ApplyTune()
    {
        _tuneRefused = !_boost.ApplyTuning();
        _changed();
        Refresh();
    }

    void ShowTuneValues()
    {
        CapValue.Text = CapIsOff ? Loc.T("TuneNoCap") : $"{CapSlider.Value:0} MHz";
        CapValue.Foreground = CapIsOff ? (Brush)FindResource("Muted") : (Brush)FindResource("Text");
        OffsetValue.Text = $"+{OffsetSlider.Value:0} MHz";
        OffsetValue.Foreground = OffsetSlider.Value == 0 ? (Brush)FindResource("Muted") : (Brush)FindResource("Text");
        OffsetSlider.IsEnabled = !CapIsOff;
        OffsetNeedsCap.Visibility = CapIsOff ? Visibility.Visible : Visibility.Collapsed;
    }

    void RefreshTuning()
    {
        var gpu = _boost.Gpu;
        float? max = gpu?.MaxGraphicsClock;
        string? unavailable = _boost.GpuPending ? Loc.T("GpuLooking")
            : gpu is null ? Loc.T("TuneNoNvidia")
            : max is null ? Loc.T("TuneLocked") : null;
        bool unlocked = _settings.GpuTuningUnlocked && unavailable is null;

        TuneUnlock.IsEnabled = unavailable is null;
        TuneUnavailable.Text = unavailable ?? "";
        TuneUnavailable.Visibility = unavailable is null ? Visibility.Collapsed : Visibility.Visible;
        TuneWarning.Visibility = unlocked || unavailable != null ? Visibility.Collapsed : Visibility.Visible;
        TuneBody.Visibility = unlocked ? Visibility.Visible : Visibility.Collapsed;
        TuneGuide.Visibility = unavailable is null ? Visibility.Visible : Visibility.Collapsed; // readable before unlocking

        var tuner = unavailable is null ? GameBoost.OtherTunerRunning() : null;
        TuneConflict.Visibility = tuner is null ? Visibility.Collapsed : Visibility.Visible;
        if (tuner != null) TuneConflictText.Text = string.Format(Loc.T("TuneConflict"), tuner);

        if (!unlocked || gpu is null || max is not { } top) return;

        if (!_tuneReady)
        {
            // ticks count from Minimum: keep it a whole number of 15 MHz steps below the top, so the top ("no cap") is reachable
            int steps = (int)Math.Min(60, Math.Max(1, (top - 300) / 15));
            CapSlider.Maximum = top;
            CapSlider.Minimum = top - steps * 15;
            if (gpu.ClockOffsetRange is { } r)
                OffsetSlider.Maximum = Math.Max(15, Math.Min(r.Max, GameBoost.MaxOffset) / 15 * 15);
            else
            {
                OffsetRow.Visibility = Visibility.Collapsed;
                OffsetUnsupported.Visibility = Visibility.Visible;
            }
            _tuneReady = true;
        }

        // follow the settings unless a change is on its way (a crash or "Undo" puts them back to stock)
        if (!_applyTune.IsEnabled)
        {
            _syncing = true;
            CapSlider.Value = _settings.GpuClockCap ?? CapSlider.Maximum;
            OffsetSlider.Value = _settings.GpuClockOffset;
            _syncing = false;
        }
        ShowTuneValues();

        var clock = _boost.GameClock;
        UseGameClock.IsEnabled = clock != null;
        UseGameClockText.Text = clock is { } c ? string.Format(Loc.T("TuneUseGameClock"), c) : Loc.T("TuneNoGameClock");

        // where it stands
        Brush color;
        string title, text;
        bool keep = false, undo = false;
        if (_boost.TuningCrashedAt is { } at)
        {
            (color, title, text) = (Bad, Loc.T("TuneCrashed"), string.Format(Loc.T("TuneCrashedText"), at.ToString("HH:mm")));
        }
        else if (!_boost.TuningApplied)
        {
            (color, title, text) = _boost.TuningDroppedAtStart
                ? (Warn, Loc.T("TuneDropped"), Loc.T("TuneDroppedText"))
                : ((Brush)FindResource("Muted"), Loc.T("TuneStock"), Loc.T("TuneStockText"));
        }
        else if (!_settings.GpuTuningTested)
        {
            (color, title, text) = (Warn, string.Format(Loc.T("TuneTesting"), _boost.TuningSince.ToString("HH:mm")), Loc.T("TuneTestingText"));
            keep = undo = true;
        }
        else
        {
            (color, title, text) = (Good, Loc.T("TuneKept"), Loc.T("TuneKeptText"));
            undo = true;
        }
        if (_tuneRefused) (color, text) = (Bad, Loc.T("TuneRefused"));

        TuneDot.Fill = color;
        TuneStatusTitle.Text = title;
        TuneStatusTitle.Foreground = color;
        TuneStatusText.Text = text;
        TuneStatus.Background = Wash(color, 0x12);
        TuneStatus.BorderBrush = Wash(color, 0x33);
        TuneActions.Visibility = keep || undo ? Visibility.Visible : Visibility.Collapsed;
        TuneKeep.Visibility = keep ? Visibility.Visible : Visibility.Collapsed;
        TuneUndo.Content = Loc.T(keep ? "TuneUndo" : "PowerReset");
        TuneUndo.Margin = new Thickness(keep ? 8 : 0, 0, 0, 0);

        LiveClock.Text = gpu.GraphicsClock is { } now ? $"{now:0} MHz" : "–";
        LivePower.Text = gpu.PowerWatts is { } w ? $"{w:0} W" : "–";
        LiveTemp.Text = gpu.Temperature is { } t ? $"{t:0}°C" : "–";
    }

    // ───────────────────────── helpers ─────────────────────────

    static Brush Wash(Brush color, byte alpha)
    {
        var c = ((SolidColorBrush)color).Color;
        var b = new SolidColorBrush(Color.FromArgb(alpha, c.R, c.G, c.B));
        b.Freeze();
        return b;
    }

    static Brush Frozen(byte r, byte g, byte b)
    {
        var brush = new SolidColorBrush(Color.FromRgb(r, g, b));
        brush.Freeze();
        return brush;
    }
}
