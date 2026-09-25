using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime;
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
    /// <summary>A second launch sets this, and the running Pulse opens its window.</summary>
    const string ShowSignalName = "Pulse.Overlay.ShowSettings";

    Mutex? _mutex;
    bool _ownsMutex;
    EventWaitHandle? _showSignal;
    RegisteredWaitHandle? _showWait;
    AppSettings? _settings;
    SensorService? _sensors;
    FpsService? _fps;
    OverlayWindow? _window;
    SettingsWindow? _settingsWindow;
    HotkeyManager? _hotkeys;
    Forms.NotifyIcon? _tray;
    Forms.ContextMenuStrip? _trayMenu;
    DispatcherTimer? _timer;
    bool _hintedTray;
    bool _exiting;
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

        // "--tray" = started at sign-in: straight to the overlay, no window.
        bool fromLogon = Array.Exists(e.Args, a => a.Equals("--tray", StringComparison.OrdinalIgnoreCase));

        _mutex = new Mutex(true, "Pulse.Overlay.SingleInstance", out _ownsMutex);
        if (!_ownsMutex)
        {
            // Already running: bring its window up instead of silently doing nothing.
            if (!fromLogon && EventWaitHandle.TryOpenExisting(ShowSignalName, out var signal))
                using (signal) signal.Set();
            Shutdown();
            return;
        }

        _showSignal = new EventWaitHandle(false, EventResetMode.AutoReset, ShowSignalName);
        _showWait = ThreadPool.RegisterWaitForSingleObject(_showSignal,
            (_, _) => Dispatcher.BeginInvoke(new Action(ShowSettings)), null, Timeout.Infinite, executeOnlyOnce: false);

        DispatcherUnhandledException += OnUnhandled;

        // The overlay is tiny, so draw it on the CPU. It never creates a D3D device,
        // never competes with the game for GPU time, and never shows up in PresentMon.
        RenderOptions.ProcessRenderMode = System.Windows.Interop.RenderMode.SoftwareOnly;

        // Below-normal priority + Windows "efficiency mode" (EcoQoS → E-cores on hybrid CPUs).
        Native.ApplyLowImpactMode();

        _settings = AppSettings.Load();
        Loc.Instance.Language = _settings.Language;

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

        // One UI tick every 500 ms. Background priority = it yields to everything else.
        _timer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(500) };
        _timer.Tick += OnTick;
        _timer.Start();

        SystemEvents.DisplaySettingsChanged += OnDisplayChanged;

        if (fromLogon || !_settings.ShowSettingsOnLaunch) ShowOverlay();
        else ShowSettings();

        if (failed.Count > 0)
            _tray?.ShowBalloonTip(4000, "Pulse", Loc.T("HotkeysTaken") + string.Join(", ", failed), Forms.ToolTipIcon.Warning);
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
        if (_window is null) return;
        if (_window.IsVisible) HideOverlay();
        else ShowOverlay();
    }

    void ShowOverlay()
    {
        if (_window is null || _sensors is null) return;
        _sensors.Paused = false;
        _window.Show();
        _window.EnsureTopmost();
        OnTick(null, EventArgs.Empty);
    }

    void HideOverlay()
    {
        _window?.Hide();
        UpdateSensorPause();
    }

    // Hidden overlay and no window open = no sensor polling at all.
    void UpdateSensorPause()
    {
        if (_sensors != null)
            _sensors.Paused = _window?.IsVisible != true && _settingsWindow?.IsVisible != true;
    }

    void ShowSettings()
    {
        if (_settings is null || _sensors is null) return;
        _sensors.Paused = false; // the preview borrows the real CPU / GPU names

        if (_settingsWindow is null)
        {
            var sensors = _sensors;
            _settingsWindow = new SettingsWindow(_settings, () => sensors.Latest, () => _window?.IsVisible == true);
            _settingsWindow.Changed += Commit;
            _settingsWindow.LanguageChanged += UpdateTrayLanguage;
            _settingsWindow.LaunchRequested += () =>
            {
                ShowOverlay();
                _settingsWindow?.Close();
            };
            _settingsWindow.QuitRequested += Quit;
            _settingsWindow.Closed += OnSettingsClosed;
        }

        if (_settingsWindow.WindowState == WindowState.Minimized) _settingsWindow.WindowState = WindowState.Normal;
        _settingsWindow.Show();
        _settingsWindow.Activate();
    }

    // Closed, not hidden: the preview's bitmaps (tens of MB) must not live on next to a game.
    void OnSettingsClosed(object? sender, EventArgs e)
    {
        _settingsWindow = null;
        if (_exiting) return;

        UpdateSensorPause();

        // WPF lets go of a closed window a moment later (input / render bookkeeping),
        // so collect a few seconds on, once it is truly unreachable.
        var later = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromSeconds(4) };
        later.Tick += (_, _) =>
        {
            later.Stop();
            GCSettings.LargeObjectHeapCompactionMode = GCLargeObjectHeapCompactionMode.CompactOnce;
            GC.Collect();
            GC.WaitForPendingFinalizers(); // bitmaps free their native pixels in finalizers
            GC.Collect();
        };
        later.Start();

        if (_window?.IsVisible == true || _hintedTray) return;
        _hintedTray = true;
        _tray?.ShowBalloonTip(3000, "Pulse", Loc.T("StillRunning"), Forms.ToolTipIcon.Info);
    }

    void Quit()
    {
        _exiting = true;
        Shutdown();
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
        _settings.Corner = (Corner)(((int)_settings.Corner + 1) % Enum.GetValues<Corner>().Length);
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
        _trayMenu = new Forms.ContextMenuStrip { ShowCheckMargin = true, ShowImageMargin = false };

        var miSettings = Item("TraySettings", ShowSettings);
        miSettings.Font = new System.Drawing.Font(miSettings.Font, System.Drawing.FontStyle.Bold);

        var miToggle = Item("TrayToggle", ToggleOverlay);
        miToggle.ShortcutKeyDisplayString = "Ctrl+Shift+O";

        var miCompact = Item("TrayCompact", ToggleCompact);
        miCompact.ShortcutKeyDisplayString = "Ctrl+Shift+L";

        var miCorner = Choice("TrayCorner", Array.ConvertAll(Enum.GetValues<Corner>(), c => (c.ToString(), c)),
            () => _settings!.Corner, v => { _settings!.Corner = v; Commit(); });

        var miExit = Item("TrayExit", Quit);

        _trayMenu.Items.AddRange(new Forms.ToolStripItem[]
        {
            miSettings, new Forms.ToolStripSeparator(),
            miToggle, miCompact, miCorner, new Forms.ToolStripSeparator(),
            miExit,
        });
        _trayMenu.Opening += (_, _) =>
        {
            if (_settings != null) miCompact.Checked = _settings.Compact;
            miToggle.Checked = _window?.IsVisible == true;
        };
        UpdateTrayLanguage();

        _tray = new Forms.NotifyIcon
        {
            Icon = TrayIconFactory.Create(),
            Text = "Pulse  (Ctrl+Shift+O)",
            ContextMenuStrip = _trayMenu,
            Visible = true,
        };
        _tray.MouseClick += (_, e) => { if (e.Button == Forms.MouseButtons.Left) ToggleOverlay(); };
        _tray.MouseDoubleClick += (_, e) => { if (e.Button == Forms.MouseButtons.Left) ShowSettings(); };
    }

    /// <summary>Every tray item keeps its string key in Tag; re-read them after a language switch.</summary>
    void UpdateTrayLanguage()
    {
        if (_trayMenu is null) return;
        _trayMenu.RightToLeft = Loc.Instance.Language == AppLanguage.Persian ? Forms.RightToLeft.Yes : Forms.RightToLeft.No;
        foreach (Forms.ToolStripItem item in _trayMenu.Items) Relabel(item);

        static void Relabel(Forms.ToolStripItem item)
        {
            if (item.Tag is string key) item.Text = Loc.T(key);
            if (item is Forms.ToolStripMenuItem mi)
                foreach (Forms.ToolStripItem sub in mi.DropDownItems) Relabel(sub);
        }
    }

    static Forms.ToolStripMenuItem Item(string key, Action onClick)
    {
        var item = new Forms.ToolStripMenuItem(Loc.T(key)) { Tag = key };
        item.Click += (_, _) => onClick();
        return item;
    }

    static Forms.ToolStripMenuItem Choice<T>(string key, (string Key, T Value)[] options, Func<T> current, Action<T> apply)
        where T : notnull
    {
        var parent = new Forms.ToolStripMenuItem(Loc.T(key)) { Tag = key };
        var values = new Dictionary<Forms.ToolStripMenuItem, T>();
        foreach (var (label, value) in options)
        {
            var item = Item(label, () => apply(value));
            values[item] = value;
            parent.DropDownItems.Add(item);
        }
        parent.DropDownOpening += (_, _) =>
        {
            foreach (var (item, value) in values) item.Checked = Equals(value, current());
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
        _showWait?.Unregister(null);
        _showSignal?.Dispose();
        _exiting = true;
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
