using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Microsoft.Win32;
using Pulse.Services;
using Forms = System.Windows.Forms;

namespace Pulse;

public partial class App : Application
{
    Mutex? _mutex;
    bool _ownsMutex;
    AppSettings? _settings;
    SensorService? _sensors;
    FpsService? _fps;
    OverlayWindow? _window;
    HotkeyManager? _hotkeys;
    Forms.NotifyIcon? _tray;
    Forms.ToolStripMenuItem? _miCompact;
    DispatcherTimer? _timer;
    int _tick;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // Switches used by the installer / uninstaller — do the job and quit, no UI.
        if (e.Args.Length > 0)
        {
            switch (e.Args[0].ToLowerInvariant())
            {
                case "--register-startup": StartupTask.Enable(); Shutdown(); return;
                case "--unregister-startup": StartupTask.Disable(); Shutdown(); return;
            }
        }

        _mutex = new Mutex(true, "Pulse.Overlay.SingleInstance", out _ownsMutex);
        if (!_ownsMutex) { Shutdown(); return; }

        DispatcherUnhandledException += OnUnhandled;

        // The overlay is tiny, so draw it on the CPU. It never creates a D3D device,
        // never competes with the game for GPU time, and never shows up in PresentMon.
        RenderOptions.ProcessRenderMode = System.Windows.Interop.RenderMode.SoftwareOnly;

        // Below-normal priority + Windows "efficiency mode" (EcoQoS → E-cores on hybrid CPUs).
        Native.ApplyLowImpactMode();

        _settings = AppSettings.Load();

        _sensors = new SensorService();
        _sensors.Start();

        _fps = new FpsService(AppSettings.Resolve(_settings.PresentMonPath), _settings.PresentMonArgs);
        _fps.Start();

        _window = new OverlayWindow(_settings);
        BuildTray();

        _hotkeys = new HotkeyManager(_window);
        var failed = new List<string>();
        if (!_hotkeys.Register(ModifierKeys.Control | ModifierKeys.Shift, Key.O, ToggleOverlay)) failed.Add("Ctrl+Shift+O");
        if (!_hotkeys.Register(ModifierKeys.Control | ModifierKeys.Shift, Key.L, ToggleCompact)) failed.Add("Ctrl+Shift+L");
        if (!_hotkeys.Register(ModifierKeys.Control | ModifierKeys.Shift, Key.P, CycleCorner)) failed.Add("Ctrl+Shift+P");

        _window.Show();

        // One UI tick every 500 ms. Background priority = it yields to everything else.
        _timer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(500) };
        _timer.Tick += OnTick;
        _timer.Start();

        SystemEvents.DisplaySettingsChanged += OnDisplayChanged;

        if (failed.Count > 0)
            _tray?.ShowBalloonTip(4000, "Pulse", "این کلیدها را برنامه‌ی دیگری گرفته: " + string.Join(", ", failed), Forms.ToolTipIcon.Warning);
        else if (_fps.Error is { } err)
            _tray?.ShowBalloonTip(5000, "Pulse", err, Forms.ToolTipIcon.Info);
    }

    void OnTick(object? sender, EventArgs e)
    {
        if (_window is null || _fps is null || _sensors is null || !_window.IsVisible) return;

        _fps.UpdateTarget();
        _window.ViewModel.Apply(_sensors.Latest, _fps.GetStats(), _fps.TargetName, _fps.Error);

        // Some games re-assert their own z-order; nudge ours back every ~3 s.
        if (++_tick % 6 == 0) _window.EnsureTopmost();
    }

    // ───────────────────────── actions ─────────────────────────

    void ToggleOverlay()
    {
        if (_window is null || _sensors is null) return;
        if (_window.IsVisible)
        {
            _window.Hide();
            _sensors.Paused = true;       // hidden overlay = no sensor polling at all
        }
        else
        {
            _sensors.Paused = false;
            _window.Show();
            _window.EnsureTopmost();
            OnTick(null, EventArgs.Empty);
        }
    }

    void ToggleCompact()
    {
        if (_settings is null) return;
        _settings.Compact = !_settings.Compact;
        Commit();
    }

    void CycleCorner()
    {
        if (_settings is null) return;
        _settings.Corner = (Corner)(((int)_settings.Corner + 1) % 4);
        Commit();
    }

    void Commit()
    {
        _window?.ApplySettings();
        _settings?.Save();
    }

    // ───────────────────────── tray ─────────────────────────

    void BuildTray()
    {
        var menu = new Forms.ContextMenuStrip { RightToLeft = Forms.RightToLeft.Yes, ShowCheckMargin = true, ShowImageMargin = false };

        var miToggle = new Forms.ToolStripMenuItem("نمایش / پنهان") { ShortcutKeyDisplayString = "Ctrl+Shift+O" };
        miToggle.Click += (_, _) => ToggleOverlay();

        _miCompact = new Forms.ToolStripMenuItem("حالت فشرده") { ShortcutKeyDisplayString = "Ctrl+Shift+L" };
        _miCompact.Click += (_, _) => ToggleCompact();

        var miCorner = Choice("گوشه‌ی صفحه", new[]
        {
            ("بالا چپ", Corner.TopLeft), ("بالا راست", Corner.TopRight),
            ("پایین چپ", Corner.BottomLeft), ("پایین راست", Corner.BottomRight),
        }, () => _settings!.Corner, v => { _settings!.Corner = v; Commit(); });

        var miScale = Choice("اندازه", new[]
        {
            ("کوچک", 0.85), ("معمولی", 1.0), ("بزرگ", 1.15), ("خیلی بزرگ", 1.3),
        }, () => _settings!.Scale, v => { _settings!.Scale = v; Commit(); });

        var miOpacity = Choice("پس‌زمینه", new[]
        {
            ("شفاف", 0.6), ("نیمه‌شفاف", 0.78), ("تقریباً مات", 0.92), ("مات", 1.0),
        }, () => _settings!.BackgroundOpacity, v => { _settings!.BackgroundOpacity = v; Commit(); });

        var miStartup = new Forms.ToolStripMenuItem("اجرا همراه ویندوز");
        miStartup.Click += (_, _) =>
        {
            if (StartupTask.IsEnabled()) StartupTask.Disable();
            else StartupTask.Enable();
        };

        var miExit = new Forms.ToolStripMenuItem("خروج");
        miExit.Click += (_, _) => Shutdown();

        menu.Items.AddRange(new Forms.ToolStripItem[]
        {
            miToggle, _miCompact, new Forms.ToolStripSeparator(),
            miCorner, miScale, miOpacity, new Forms.ToolStripSeparator(),
            miStartup, miExit,
        });
        menu.Opening += (_, _) =>
        {
            if (_miCompact != null && _settings != null) _miCompact.Checked = _settings.Compact;
            miStartup.Checked = StartupTask.IsEnabled();
        };

        _tray = new Forms.NotifyIcon
        {
            Icon = TrayIconFactory.Create(),
            Text = "Pulse  (Ctrl+Shift+O)",
            ContextMenuStrip = menu,
            Visible = true,
        };
        _tray.MouseClick += (_, e) => { if (e.Button == Forms.MouseButtons.Left) ToggleOverlay(); };
    }

    static Forms.ToolStripMenuItem Choice<T>(string text, (string Label, T Value)[] options, Func<T> current, Action<T> apply)
        where T : notnull
    {
        var parent = new Forms.ToolStripMenuItem(text);
        foreach (var (label, value) in options)
        {
            var item = new Forms.ToolStripMenuItem(label) { Tag = value };
            item.Click += (_, _) => apply(value);
            parent.DropDownItems.Add(item);
        }
        parent.DropDownOpening += (_, _) =>
        {
            foreach (Forms.ToolStripItem i in parent.DropDownItems)
                if (i is Forms.ToolStripMenuItem mi) mi.Checked = Equals(mi.Tag, current());
        };
        return parent;
    }

    // ───────────────────────── plumbing ─────────────────────────

    void OnDisplayChanged(object? sender, EventArgs e) =>
        Dispatcher.InvokeAsync(() => _window?.Reposition());

    void OnUnhandled(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        try
        {
            Directory.CreateDirectory(AppSettings.Folder);
            File.AppendAllText(Path.Combine(AppSettings.Folder, "error.log"),
                $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {e.Exception}{Environment.NewLine}");
        }
        catch { /* never crash while logging a crash */ }
        e.Handled = true;
    }

    protected override void OnExit(ExitEventArgs e)
    {
        SystemEvents.DisplaySettingsChanged -= OnDisplayChanged;
        _timer?.Stop();
        _hotkeys?.Dispose();

        if (_tray != null)
        {
            _tray.Visible = false;
            _tray.Icon?.Dispose();
            _tray.Dispose();
        }

        _fps?.Dispose();
        _sensors?.Dispose();
        _settings?.Save();

        if (_ownsMutex) _mutex?.ReleaseMutex();
        _mutex?.Dispose();

        base.OnExit(e);
    }
}
